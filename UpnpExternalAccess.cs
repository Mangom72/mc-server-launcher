using System.Threading.Tasks;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

internal static partial class Launcher
{
	private sealed class ExternalPortCheckResult
	{
		public bool CheckCompleted;
		public bool Reachable;
		public string PublicIp;
		public string Error;
	}

	private sealed class CreatedUpnpMapping
	{
		public int ExternalPort;
		public int InternalPort;
		public string Protocol;
		public string InternalClient;
		public string Description;
	}

	private sealed class UpnpMappingAttempt
	{
		public object NatObject;
		public object Collection;
		public readonly List<CreatedUpnpMapping> Created = new List<CreatedUpnpMapping>();
		public string RouterExternalIp;
		public bool PortConflict;
		public bool ExistingMatchingMapping;
		public bool UdpRequired;
		public bool UdpMapped;
		public string Error;
	}

	internal sealed class UpnpCleanupResult
	{
		public int ClearedCount;
		public string Error;
		public bool TimedOut;
	}

	private static NetworkToolsForm activeManualNetworkForm;
	private static string lastUpnpCleanupStatus;
	private static int externalRecheckRunning;
	private static readonly object activeUpnpOwnershipLock = new object();
	private static readonly HashSet<string> activeUpnpOwnershipKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
	private static readonly Mutex upnpTrackerMutex = new Mutex(false, "Local\\MineHarbor-UpnpTracker-v1");
	private const int UpnpDiscoveryAttemptCount = 3;
	private const int UpnpMappingAttemptCount = 3;
	private const int UpnpMappingVerificationCount = 3;

	private static void ConfigureExternalAccessThread(Thread thread)
	{
		if (thread == null) throw new ArgumentNullException("thread");
		thread.SetApartmentState(ApartmentState.STA);
	}

	private static void RunExternalAccessPipeline(int serverPort, string javaPath, string serverDirectory, WaitHandle serverStopped)
	{
		ReportExternalAccessStatus("서버 포트 확인 중", false);
		if (!WaitForLocalServerPort(serverPort, serverStopped, TimeSpan.FromMinutes(3.0)))
		{
			if (!serverStopped.WaitOne(0))
			{
				Console.WriteLine("[외부 접속] 로컬 TCP 포트 " + serverPort + "가 3분 안에 열리지 않았습니다.");
				ShowLauncherNotice(LauncherUiText("서버 포트가 열리지 않았습니다. 콘솔을 확인해 주세요.", "The server port did not open. Check the console."), true);
			}
			return;
		}
		NotifyServerReady(serverPort);
		if (serverStopped.WaitOne(1500))
		{
			return;
		}

		ReportExternalAccessStatus("기존 포트포워딩 검사 중", false);
		ExternalPortCheckResult initial = CheckExternalPort(serverPort, serverStopped, 3);
		if (serverStopped.WaitOne(0))
		{
			return;
		}
		if (initial.Reachable)
		{
			string address = FormatExternalAddress(initial.PublicIp, serverPort);
			SetLauncherConnectionAddress(address);
			Console.WriteLine("[외부 접속] 기존 포트포워딩 정상: " + address);
			ReportExternalAccessStatus("기존 포트포워딩 정상 · " + address, false);
			return;
		}
		if (!initial.CheckCompleted)
		{
			Console.WriteLine("[외부 접속] 검사 서비스에 연결하지 못해 UPnP를 실행하지 않습니다: " + initial.Error);
			ShowLauncherNotice(LauncherUiText("외부 접속 상태를 확인하지 못해 UPnP를 실행하지 않았습니다.", "External reachability could not be checked, so UPnP was not attempted."), true);
			return;
		}

		ReportExternalAccessStatus("외부 접속 실패", true);
		Console.WriteLine("[외부 접속] 기존 설정으로 외부에서 접속할 수 없습니다. UPnP를 확인합니다.");
		if (!string.IsNullOrWhiteSpace(initial.Error))
		{
			Console.WriteLine("[외부 접속] 최초 검사 정보: " + initial.Error);
		}
		NetworkDetails network = GetNetworkDetails();
		bool firewallCheckNeeded = !HasLikelyWindowsFirewallAllowRule(serverPort, 6);
		bool udpNeeded = IsUdpMappingNeeded(serverDirectory);
		ReportExternalAccessStatus("UPnP 장치 검색 중", false);
		UpnpMappingAttempt attempt = TryCreateUpnpMappings(serverPort, network, udpNeeded, serverStopped);
		if (attempt.Collection == null)
		{
			ReportExternalAccessStatus("UPnP 매핑 실패", true);
			bool cgnatWithoutDevice = IsCgnatPossible(attempt.RouterExternalIp, initial.PublicIp);
			if (cgnatWithoutDevice)
			{
				ReportExternalAccessStatus("CGNAT 가능성 있음", true);
			}
			ShowManualPortForwardingWindow(serverDirectory, serverPort, javaPath, initial.PublicIp, cgnatWithoutDevice, firewallCheckNeeded, attempt.Error);
			PrintPortForwardingGuide(initial.PublicIp, serverPort, javaPath);
			ReleaseUpnpObjects(attempt);
			return;
		}

		try
		{
			if (attempt.PortConflict)
			{
				ReportExternalAccessStatus("포트 충돌 발생", true);
				bool cgnatConflict = IsCgnatPossible(attempt.RouterExternalIp, initial.PublicIp);
				ShowManualPortForwardingWindow(serverDirectory, serverPort, javaPath, initial.PublicIp, cgnatConflict, firewallCheckNeeded, attempt.Error);
				PrintPortForwardingGuide(initial.PublicIp, serverPort, javaPath);
				return;
			}

			if (attempt.Created.Count > 0 && (!attempt.UdpRequired || attempt.UdpMapped))
			{
				ReportExternalAccessStatus("UPnP 매핑 성공", false);
			}
			else if (attempt.Created.Count > 0)
			{
				ReportExternalAccessStatus("UPnP TCP 매핑만 성공", true);
			}
			else if (attempt.ExistingMatchingMapping)
			{
				Console.WriteLine("[UPnP] 같은 PC를 가리키는 기존 매핑이 있어 변경하지 않았습니다.");
			}
			else
			{
				ReportExternalAccessStatus("UPnP 매핑 실패", true);
			}

			ReportExternalAccessStatus("외부 접속 재검사 중", false);
			if (serverStopped.WaitOne(2000))
			{
				return;
			}
			ExternalPortCheckResult recheck = CheckExternalPort(serverPort, serverStopped, 5);
			if (recheck.Reachable)
			{
				string address = FormatExternalAddress(recheck.PublicIp, serverPort);
				SetLauncherConnectionAddress(address);
				Console.WriteLine("[외부 접속] UPnP 처리 후 외부 접속 성공: " + address);
				bool completeMapping = attempt.Created.Count > 0 && (!attempt.UdpRequired || attempt.UdpMapped);
				ReportExternalAccessStatus(completeMapping ? "UPnP 매핑 성공 · " + address : attempt.Created.Count > 0 ? "UPnP TCP 매핑만 성공 · " + address : "기존 포트포워딩 정상 · " + address, !completeMapping && attempt.Created.Count > 0);
			}
			else
			{
				string publicIp = string.IsNullOrWhiteSpace(recheck.PublicIp) ? initial.PublicIp : recheck.PublicIp;
				bool cgnatPossible = IsCgnatPossible(attempt.RouterExternalIp, publicIp);
				if (cgnatPossible)
				{
					ReportExternalAccessStatus("CGNAT 가능성 있음", true);
				}
				ReportExternalAccessStatus("수동 포트포워딩 필요", true);
				string reason = string.IsNullOrWhiteSpace(attempt.Error) ? recheck.Error : attempt.Error + " / " + recheck.Error;
				ShowManualPortForwardingWindow(serverDirectory, serverPort, javaPath, publicIp, cgnatPossible, firewallCheckNeeded, reason);
				PrintPortForwardingGuide(publicIp, serverPort, javaPath);
			}

		}
		finally
		{
			if (attempt.Created.Count > 0)
			{
				if (!serverStopped.WaitOne(0))
				{
					serverStopped.WaitOne();
				}
				DeleteCreatedUpnpMappings(attempt);
			}
			ReleaseUpnpObjects(attempt);
		}
	}

	private static void RecheckExternalReachabilityOnly(int serverPort)
	{
		if (Interlocked.CompareExchange(ref externalRecheckRunning, 1, 0) != 0)
		{
			ReportExternalAccessStatus("외부 접속 재검사 진행 중", false);
			return;
		}
		ReportExternalAccessStatus("기존 포트포워딩 검사 중", false);
		try
		{
			using (ManualResetEvent notStopped = new ManualResetEvent(false))
			{
				ExternalPortCheckResult result = CheckExternalPort(serverPort, notStopped, 1);
				if (result.Reachable)
				{
					string address = FormatExternalAddress(result.PublicIp, serverPort);
					SetLauncherConnectionAddress(address);
					ReportExternalAccessStatus("기존 포트포워딩 정상 · " + address, false);
				}
				else if (!result.CheckCompleted)
				{
					ReportExternalAccessStatus("외부 접속 확인 불가", true);
				}
				else
				{
					ReportExternalAccessStatus("외부 접속 실패", true);
				}
			}
		}
		finally { Interlocked.Exchange(ref externalRecheckRunning, 0); }
	}

	private static bool WaitForLocalServerPort(int port, WaitHandle stopped, TimeSpan timeout)
	{
		DateTime deadline = DateTime.UtcNow.Add(timeout);
		while (DateTime.UtcNow < deadline)
		{
			if (IsLocalTcpPortListening(port))
			{
				return true;
			}
			if (stopped.WaitOne(1000))
			{
				return false;
			}
		}
		return false;
	}

	private static ExternalPortCheckResult CheckExternalPort(int port, WaitHandle stopped, int attempts)
	{
		ExternalPortCheckResult result = new ExternalPortCheckResult();
		try
		{
			ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
			result.PublicIp = DownloadExternalCheckText("https://portchecker.io/api/me", stopped, 2).Trim();
			IPAddress parsed;
			if (!IPAddress.TryParse(result.PublicIp, out parsed) || IPAddress.IsLoopback(parsed))
			{
				throw new InvalidDataException("외부 검사 서버의 공인 IP 응답을 인식할 수 없습니다.");
			}
			for (int attempt = 1; attempt <= Math.Max(1, attempts); attempt++)
			{
				if (stopped.WaitOne(0))
				{
					return result;
				}
				string response = DownloadExternalCheckText("https://portchecker.io/api/me/" + port, stopped, 2).Trim();
				bool reachable;
				if (!bool.TryParse(response, out reachable))
				{
					throw new InvalidDataException("외부 검사 서버의 포트 응답을 인식할 수 없습니다.");
				}
				result.CheckCompleted = true;
				if (reachable)
				{
					result.Reachable = true;
					break;
				}
				if (attempt < attempts && stopped.WaitOne(3000))
				{
					break;
				}
			}
		}
		catch (Exception exception)
		{
			result.Error = SummarizeUpnpError(exception);
		}
		return result;
	}

	private static string DownloadExternalCheckText(string url, WaitHandle stopped, int attempts)
	{
		Exception lastError = null;
		for (int attempt = 1; attempt <= Math.Max(1, attempts); attempt++)
		{
			if (stopped.WaitOne(0))
			{
				throw new OperationCanceledException("서버가 종료되어 외부 접속 검사를 중단했습니다.");
			}
			try
			{
				return DownloadText(url);
			}
			catch (Exception exception)
			{
				lastError = exception;
				if (attempt < attempts)
				{
					Console.WriteLine("[외부 접속] 검사 서비스 연결 재시도 " + (attempt + 1).ToString(CultureInfo.InvariantCulture) + "/" + attempts.ToString(CultureInfo.InvariantCulture));
					if (stopped.WaitOne(attempt * 1000))
					{
						throw new OperationCanceledException("서버가 종료되어 외부 접속 검사를 중단했습니다.");
					}
				}
			}
		}
		throw lastError ?? new WebException("외부 접속 검사 서비스에 연결하지 못했습니다.");
	}


	private static UpnpMappingAttempt TryCreateUpnpMappings(int serverPort, NetworkDetails network, bool udpNeeded, WaitHandle stopped)
	{
		UpnpMappingAttempt result = new UpnpMappingAttempt();
		if (network == null || string.IsNullOrWhiteSpace(network.LocalIpv4))
		{
			result.Error = "현재 PC의 로컬 IPv4 주소를 확인하지 못했습니다.";
			return result;
		}
		try
		{
			if (stopped.WaitOne(0))
			{
				result.Error = "서버가 종료되어 UPnP 매핑을 중단했습니다.";
				return result;
			}
			if (!TryDiscoverUpnpCollection(result, stopped))
			{
				return result;
			}
			int recovered = CleanupTrackedComMappings(result.Collection);
			if (recovered > 0) Console.WriteLine("[UPnP] 이전 실행에서 남은 매핑 " + recovered.ToString(CultureInfo.InvariantCulture) + "개를 안전하게 정리했습니다.");
			ReportExternalAccessStatus("UPnP 자동 매핑 중", false);
			string token = Guid.NewGuid().ToString("N").Substring(0, 12);
			string description = "MH-" + token;
			result.UdpRequired = udpNeeded;
			if (!TryAddSingleUpnpMapping(result, serverPort, serverPort, "TCP", network.LocalIpv4, description, stopped))
			{
				return result;
			}
			if (udpNeeded)
			{
				if (!TryAddSingleUpnpMapping(result, serverPort, serverPort, "UDP", network.LocalIpv4, description, stopped))
				{
					if (result.PortConflict)
					{
						ReportExternalAccessStatus("포트 충돌 발생", true);
						result.PortConflict = false;
					}
					result.Error = "UDP 매핑을 만들지 못했습니다. 쿼리 기능은 외부에서 동작하지 않을 수 있습니다. " + (result.Error ?? string.Empty);
					Console.WriteLine("[UPnP] " + result.Error);
				}
				else result.UdpMapped = true;
			}
		}
		catch (Exception exception)
		{
			result.Error = SummarizeUpnpError(exception);
		}
		return result;
	}

	private static bool TryDiscoverUpnpCollection(UpnpMappingAttempt attempt, WaitHandle stopped)
	{
		Type natType = Type.GetTypeFromProgID("HNetCfg.NATUPnP", false);
		if (natType == null)
		{
			attempt.Error = "Windows UPnP NAT 구성 요소를 찾지 못했습니다.";
			return false;
		}
		string lastError = null;
		for (int index = 1; index <= UpnpDiscoveryAttemptCount; index++)
		{
			if (stopped.WaitOne(0))
			{
				attempt.Error = "서버가 종료되어 UPnP 장치 검색을 중단했습니다.";
				return false;
			}
			object natObject = null;
			object collection = null;
			try
			{
				natObject = Activator.CreateInstance(natType);
				collection = GetComProperty(natObject, "StaticPortMappingCollection");
				if (collection != null)
				{
					attempt.NatObject = natObject;
					attempt.Collection = collection;
					return true;
				}
				lastError = "UPnP를 지원하는 공유기 장치를 찾지 못했습니다.";
			}
			catch (Exception exception)
			{
				lastError = SummarizeUpnpError(exception);
			}
			ReleaseComObject(collection);
			ReleaseComObject(natObject);
			if (index < UpnpDiscoveryAttemptCount)
			{
				Console.WriteLine("[UPnP] 공유기 검색 재시도 " + (index + 1).ToString(CultureInfo.InvariantCulture) + "/" + UpnpDiscoveryAttemptCount.ToString(CultureInfo.InvariantCulture));
				if (stopped.WaitOne(index * 1000))
				{
					attempt.Error = "서버가 종료되어 UPnP 장치 검색을 중단했습니다.";
					return false;
				}
			}
		}
		attempt.Error = "UPnP 공유기 검색을 " + UpnpDiscoveryAttemptCount.ToString(CultureInfo.InvariantCulture) + "회 시도했지만 실패했습니다. " + (lastError ?? string.Empty);
		return false;
	}

	private static bool TryAddSingleUpnpMapping(UpnpMappingAttempt attempt, int externalPort, int internalPort, string protocol, string internalClient, string description, WaitHandle stopped)
	{
		for (int addAttempt = 1; addAttempt <= UpnpMappingAttemptCount; addAttempt++)
		{
			object existing = FindUpnpMapping(attempt.Collection, externalPort, protocol);
			if (existing != null)
			{
				try
				{
					return AcceptExistingUpnpMapping(attempt, existing, externalPort, internalPort, protocol, internalClient, description);
				}
				finally
				{
					ReleaseComObject(existing);
				}
			}
			if (stopped.WaitOne(0))
			{
				attempt.Error = "서버가 종료되어 UPnP 매핑을 중단했습니다.";
				return false;
			}
			object created = null;
			try
			{
				// Add 응답 전에 프로세스가 종료되어도 같은 토큰의 매핑만 복구할 수 있도록 의도를 먼저 기록합니다.
				PersistTrackedUpnpMapping(externalPort, internalPort, protocol, internalClient, description);
				created = InvokeComMethod(attempt.Collection, "Add", externalPort, protocol, internalPort, internalClient, true, description);
				if (created != null && MappingTargetsEndpoint(created, internalPort, internalClient))
				{
					RecordCreatedUpnpMapping(attempt, externalPort, internalPort, protocol, internalClient, description);
					try
					{
						attempt.RouterExternalIp = Convert.ToString(GetComProperty(created, "ExternalIPAddress"), CultureInfo.InvariantCulture);
					}
					catch
					{
						// 외부 IP 속성을 제공하지 않는 공유기도 매핑 자체는 사용할 수 있습니다.
					}
					VerifyUpnpMappingVisibility(attempt, externalPort, internalPort, protocol, internalClient, stopped);
					Console.WriteLine("[UPnP] " + protocol + " " + externalPort + " → " + internalClient + ":" + internalPort + " 매핑을 만들었습니다.");
					return true;
				}
				attempt.Error = protocol + " UPnP 매핑 생성 결과를 확인하지 못했습니다.";
			}
			catch (Exception exception)
			{
				attempt.Error = SummarizeUpnpError(exception);
			}
			finally
			{
				ReleaseComObject(created);
			}
			if (addAttempt < UpnpMappingAttemptCount)
			{
				Console.WriteLine("[UPnP] " + protocol + " 매핑 생성 재시도 " + (addAttempt + 1).ToString(CultureInfo.InvariantCulture) + "/" + UpnpMappingAttemptCount.ToString(CultureInfo.InvariantCulture));
				if (stopped.WaitOne(addAttempt * 750))
				{
					attempt.Error = "서버가 종료되어 UPnP 매핑을 중단했습니다.";
					MarkTrackedOwnershipInactive(description, protocol);
					return false;
				}
			}
		}
		MarkTrackedOwnershipInactive(description, protocol);
		return false;
	}

	private static bool AcceptExistingUpnpMapping(UpnpMappingAttempt attempt, object existing, int externalPort, int internalPort, string protocol, string internalClient, string description)
	{
		string existingClient = Convert.ToString(GetComProperty(existing, "InternalClient"), CultureInfo.InvariantCulture);
		int existingPort = Convert.ToInt32(GetComProperty(existing, "InternalPort"), CultureInfo.InvariantCulture);
		try
		{
			attempt.RouterExternalIp = Convert.ToString(GetComProperty(existing, "ExternalIPAddress"), CultureInfo.InvariantCulture);
		}
		catch
		{
			// 외부 IP 속성이 없어도 내부 대상이 같으면 기존 매핑을 사용할 수 있습니다.
		}
		if (string.Equals(existingClient, internalClient, StringComparison.OrdinalIgnoreCase) && existingPort == internalPort)
		{
			string existingDescription = string.Empty;
			try
			{
				existingDescription = Convert.ToString(GetComProperty(existing, "Description"), CultureInfo.InvariantCulture);
			}
			catch
			{
			}
			if (string.Equals(existingDescription, description, StringComparison.Ordinal))
			{
				// Add 호출이 응답 전에 성공했을 수 있으므로 런처 소유 매핑으로 회수합니다.
				RecordCreatedUpnpMapping(attempt, externalPort, internalPort, protocol, internalClient, description);
			}
			else
			{
				attempt.ExistingMatchingMapping = true;
			}
			return true;
		}
		attempt.PortConflict = true;
		attempt.Error = protocol + " " + externalPort + " 포트가 이미 " + existingClient + ":" + existingPort + "에 연결되어 있습니다.";
		return false;
	}

	private static bool MappingTargetsEndpoint(object mapping, int internalPort, string internalClient)
	{
		if (mapping == null)
		{
			return false;
		}
		string client = Convert.ToString(GetComProperty(mapping, "InternalClient"), CultureInfo.InvariantCulture);
		int port = Convert.ToInt32(GetComProperty(mapping, "InternalPort"), CultureInfo.InvariantCulture);
		return port == internalPort && string.Equals(client, internalClient, StringComparison.OrdinalIgnoreCase);
	}

	private static void RecordCreatedUpnpMapping(UpnpMappingAttempt attempt, int externalPort, int internalPort, string protocol, string internalClient, string description)
	{
		for (int i = 0; i < attempt.Created.Count; i++)
		{
			CreatedUpnpMapping current = attempt.Created[i];
			if (current.ExternalPort == externalPort && string.Equals(current.Protocol, protocol, StringComparison.OrdinalIgnoreCase))
			{
				return;
			}
		}
		CreatedUpnpMapping record = new CreatedUpnpMapping();
		record.ExternalPort = externalPort;
		record.InternalPort = internalPort;
		record.Protocol = protocol;
		record.InternalClient = internalClient;
		record.Description = description;
		attempt.Created.Add(record);
		PersistTrackedUpnpMapping(externalPort, internalPort, protocol, internalClient, description);
	}

	private static void PersistTrackedUpnpMapping(int externalPort, int internalPort, string protocol, string internalClient, string description)
	{
		EnterUpnpTrackerLock();
		try
		{
			List<UpnpMappedPort> tracked = UpnpMappingOwnershipTracker.LoadMappings();
			UpnpMappedPort persisted = new UpnpMappedPort();
			persisted.ExternalPort = externalPort;
			Process ownerProcess = Process.GetCurrentProcess();
			persisted.ProfileId = ownerProcess.Id.ToString(CultureInfo.InvariantCulture) + ":" + ownerProcess.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture);
			persisted.InternalPort = internalPort;
			persisted.InternalIp = internalClient;
			persisted.Protocol = protocol.ToUpperInvariant();
			persisted.Description = description;
			persisted.CreatedAt = DateTime.UtcNow;
			for (int i = 0; i < tracked.Count; i++)
			{
				UpnpMappedPort item = tracked[i];
				if (item.ExternalPort == externalPort && string.Equals(item.Protocol, protocol, StringComparison.OrdinalIgnoreCase) && IsTrackedOwnerAlive(item) && !string.Equals(item.Description, description, StringComparison.Ordinal))
				{
					throw new InvalidOperationException("다른 MineHarbor 실행이 같은 UPnP 포트의 소유권을 기록하고 있습니다.");
				}
			}
			tracked.RemoveAll(delegate(UpnpMappedPort item)
			{
				bool samePort = item.ExternalPort == externalPort && string.Equals(item.Protocol, protocol, StringComparison.OrdinalIgnoreCase);
				return samePort && (!IsTrackedOwnerAlive(item) || string.Equals(item.Description, description, StringComparison.Ordinal));
			});
			tracked.Add(persisted);
			UpnpMappingOwnershipTracker.SaveMappings(tracked);
			lock (activeUpnpOwnershipLock) activeUpnpOwnershipKeys.Add(GetUpnpOwnershipKey(description, protocol));
		}
		finally { upnpTrackerMutex.ReleaseMutex(); }
	}

	private static bool VerifyUpnpMappingVisibility(UpnpMappingAttempt attempt, int externalPort, int internalPort, string protocol, string internalClient, WaitHandle stopped)
	{
		for (int index = 1; index <= UpnpMappingVerificationCount; index++)
		{
			object current = FindUpnpMapping(attempt.Collection, externalPort, protocol);
			try
			{
				if (current != null && MappingTargetsEndpoint(current, internalPort, internalClient))
				{
					return true;
				}
			}
			catch
			{
			}
			finally
			{
				ReleaseComObject(current);
			}
			if (index < UpnpMappingVerificationCount && stopped.WaitOne(index * 400))
			{
				return false;
			}
		}
		Console.WriteLine("[UPnP] 공유기 목록 반영이 지연되고 있어 외부 접속 검사로 확인을 계속합니다.");
		return false;
	}

	private static object FindUpnpMapping(object collection, int externalPort, string protocol)
	{
		try
		{
			object direct = GetComProperty(collection, "Item", externalPort, protocol);
			if (direct != null)
			{
				return direct;
			}
		}
		catch
		{
			// 일부 공유기는 존재하지 않는 항목 조회를 COM 오류로 반환합니다.
		}
		IEnumerable enumerable = collection as IEnumerable;
		if (enumerable == null)
		{
			return null;
		}
		int inspected = 0;
		IEnumerator enumerator = null;
		try
		{
			enumerator = enumerable.GetEnumerator();
			while (enumerator.MoveNext())
			{
				object mapping = enumerator.Current;
				if (mapping == null) continue;
				bool match = false;
				try
				{
					int port = Convert.ToInt32(GetComProperty(mapping, "ExternalPort"), CultureInfo.InvariantCulture);
					string currentProtocol = Convert.ToString(GetComProperty(mapping, "Protocol"), CultureInfo.InvariantCulture);
					match = port == externalPort && string.Equals(currentProtocol, protocol, StringComparison.OrdinalIgnoreCase);
				}
				catch { }
				if (match) return mapping;
				ReleaseComObject(mapping);
				if (++inspected >= 4096) break;
			}
		}
		finally { ReleaseComObject(enumerator); }
		return null;
	}


	private static void DeleteCreatedUpnpMappings(UpnpMappingAttempt attempt)
	{
		bool allDeleted = true;
		for (int i = attempt.Created.Count - 1; i >= 0; i--)
		{
			CreatedUpnpMapping record = attempt.Created[i];
			object current = FindUpnpMapping(attempt.Collection, record.ExternalPort, record.Protocol);
			if (current == null)
			{
				RemoveTrackedUpnpMapping(record);
				continue;
			}
			try
			{
				string client = Convert.ToString(GetComProperty(current, "InternalClient"), CultureInfo.InvariantCulture);
				int port = Convert.ToInt32(GetComProperty(current, "InternalPort"), CultureInfo.InvariantCulture);
				string description = Convert.ToString(GetComProperty(current, "Description"), CultureInfo.InvariantCulture);
				if (!string.Equals(client, record.InternalClient, StringComparison.OrdinalIgnoreCase) || port != record.InternalPort || !string.Equals(description, record.Description, StringComparison.Ordinal))
				{
					allDeleted = false;
					Console.WriteLine("[UPnP] 매핑 소유 정보가 달라 삭제하지 않았습니다: " + record.Protocol + " " + record.ExternalPort);
					continue;
				}
				InvokeComMethod(attempt.Collection, "Remove", record.ExternalPort, record.Protocol);
				RemoveTrackedUpnpMapping(record);
			}
			catch (Exception exception)
			{
				allDeleted = false;
				Console.WriteLine("[UPnP] 매핑 삭제 실패: " + SummarizeUpnpError(exception));
			}
			finally
			{
				ReleaseComObject(current);
			}
		}
		string status = allDeleted ? "포트 매핑 삭제 완료" : "포트 매핑 삭제 실패";
		for (int i = 0; i < attempt.Created.Count; i++) MarkTrackedOwnershipInactive(attempt.Created[i].Description, attempt.Created[i].Protocol);
		Interlocked.Exchange(ref lastUpnpCleanupStatus, TranslateExternalAccessStatus(status));
		ReportExternalAccessStatus(status, !allDeleted);
	}

	private static void RemoveTrackedUpnpMapping(CreatedUpnpMapping record)
	{
		EnterUpnpTrackerLock();
		try
		{
			List<UpnpMappedPort> tracked = UpnpMappingOwnershipTracker.LoadMappings();
			tracked.RemoveAll(delegate(UpnpMappedPort item)
			{
				return item.ExternalPort == record.ExternalPort && item.InternalPort == record.InternalPort && string.Equals(item.Protocol, record.Protocol, StringComparison.OrdinalIgnoreCase) && string.Equals(item.InternalIp, record.InternalClient, StringComparison.OrdinalIgnoreCase) && string.Equals(item.Description, record.Description, StringComparison.Ordinal);
			});
			UpnpMappingOwnershipTracker.SaveMappings(tracked);
		}
		finally { upnpTrackerMutex.ReleaseMutex(); }
	}

	private static int CleanupTrackedComMappings(object collection)
	{
		EnterUpnpTrackerLock();
		try
		{
			List<UpnpMappedPort> tracked = UpnpMappingOwnershipTracker.LoadMappings();
			List<UpnpMappedPort> remaining = new List<UpnpMappedPort>();
			int cleared = 0;
			NetworkDetails network = GetNetworkDetails();
			string currentLocalIp = network == null ? null : network.LocalIpv4;
			for (int i = 0; i < tracked.Count; i++)
			{
				UpnpMappedPort record = tracked[i];
			if (IsTrackedOwnerAlive(record))
			{
				remaining.Add(record);
				continue;
			}
			bool safeRecord = record.ExternalPort >= 1 && record.ExternalPort <= 65535
				&& record.InternalPort >= 1 && record.InternalPort <= 65535
				&& (string.Equals(record.Protocol, "TCP", StringComparison.OrdinalIgnoreCase) || string.Equals(record.Protocol, "UDP", StringComparison.OrdinalIgnoreCase))
				&& !string.IsNullOrWhiteSpace(record.Description)
				&& (record.Description.StartsWith("MH-", StringComparison.Ordinal) || record.Description.StartsWith("MineHarbor ", StringComparison.Ordinal))
				&& string.Equals(record.InternalIp, currentLocalIp, StringComparison.OrdinalIgnoreCase);
			if (!safeRecord) continue;
			object current = null;
			try
			{
				current = FindUpnpMapping(collection, record.ExternalPort, record.Protocol);
				if (current == null) continue;
				string client = Convert.ToString(GetComProperty(current, "InternalClient"), CultureInfo.InvariantCulture);
				int port = Convert.ToInt32(GetComProperty(current, "InternalPort"), CultureInfo.InvariantCulture);
				string description = Convert.ToString(GetComProperty(current, "Description"), CultureInfo.InvariantCulture);
				if (!string.Equals(client, record.InternalIp, StringComparison.OrdinalIgnoreCase) || port != record.InternalPort || !string.Equals(description, record.Description, StringComparison.Ordinal))
				{
					Console.WriteLine("[UPnP] 현재 매핑 소유 정보가 달라 삭제하지 않았습니다: " + record.Protocol + " " + record.ExternalPort);
					continue;
				}
				InvokeComMethod(collection, "Remove", record.ExternalPort, record.Protocol);
				cleared++;
			}
			catch (Exception ex)
			{
				remaining.Add(record);
				Console.WriteLine("[UPnP] 기록된 매핑 정리 실패: " + SummarizeUpnpError(ex));
			}
			finally { ReleaseComObject(current); }
			}
			UpnpMappingOwnershipTracker.SaveMappings(remaining);
			return cleared;
		}
		finally { upnpTrackerMutex.ReleaseMutex(); }
	}

	private static void EnterUpnpTrackerLock()
	{
		try
		{
			if (!upnpTrackerMutex.WaitOne(10000)) throw new TimeoutException("다른 MineHarbor 프로세스가 UPnP 소유권 기록을 사용 중입니다.");
		}
		catch (AbandonedMutexException)
		{
			// 이전 프로세스가 비정상 종료된 경우 현재 스레드가 소유권을 이어받습니다.
		}
	}

	private static bool IsTrackedOwnerAlive(UpnpMappedPort record)
	{
		string ownerId = record == null ? null : record.ProfileId;
		if (string.IsNullOrWhiteSpace(ownerId)) return false;
		string[] parts = ownerId.Split(':');
		int processId;
		long startedTicks;
		if (parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out processId) || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out startedTicks)) return false;
		try
		{
			using (Process process = Process.GetProcessById(processId))
			{
				if (process.StartTime.ToUniversalTime().Ticks != startedTicks || process.HasExited) return false;
				if (processId != Process.GetCurrentProcess().Id) return true;
				lock (activeUpnpOwnershipLock) return activeUpnpOwnershipKeys.Contains(GetUpnpOwnershipKey(record.Description, record.Protocol));
			}
		}
		catch { return false; }
	}

	private static string GetUpnpOwnershipKey(string description, string protocol)
	{
		return (description ?? string.Empty) + "|" + (protocol ?? string.Empty).ToUpperInvariant();
	}

	private static void MarkTrackedOwnershipInactive(string description, string protocol)
	{
		lock (activeUpnpOwnershipLock) activeUpnpOwnershipKeys.Remove(GetUpnpOwnershipKey(description, protocol));
	}

	internal static Task<UpnpCleanupResult> ClearAllMineHarborUpnpMappingsAsync()
	{
		TaskCompletionSource<UpnpCleanupResult> completion = new TaskCompletionSource<UpnpCleanupResult>();
		Thread worker = new Thread((ThreadStart)delegate
		{
			UpnpCleanupResult result = new UpnpCleanupResult();
			UpnpMappingAttempt attempt = new UpnpMappingAttempt();
			try
			{
				using (ManualResetEvent stopped = new ManualResetEvent(false))
				{
					if (!TryDiscoverUpnpCollection(attempt, stopped)) throw new InvalidOperationException(attempt.Error);
					result.ClearedCount = CleanupTrackedComMappings(attempt.Collection);
				}
			}
			catch (Exception ex) { result.Error = SummarizeUpnpError(ex); }
			finally { ReleaseUpnpObjects(attempt); completion.TrySetResult(result); }
		});
		worker.IsBackground = true;
		worker.SetApartmentState(ApartmentState.STA);
		worker.Start();
		ThreadPool.QueueUserWorkItem(delegate
		{
			if (!worker.Join(20000)) completion.TrySetResult(new UpnpCleanupResult { TimedOut = true, Error = "UPnP 정리가 20초 안에 끝나지 않았습니다." });
		});
		return completion.Task;
	}

	private static string ConsumeUpnpCleanupStatus()
	{
		return Interlocked.Exchange(ref lastUpnpCleanupStatus, null);
	}

	private static bool IsUdpMappingNeeded(string serverDirectory)
	{
		try
		{
			Dictionary<string, string> properties = ReadSimpleProperties(System.IO.Path.Combine(serverDirectory, "server.properties"));
			bool enabled;
			return properties.ContainsKey("enable-query") && bool.TryParse(properties["enable-query"], out enabled) && enabled;
		}
		catch
		{
			return false;
		}
	}

	private static bool HasLikelyWindowsFirewallAllowRule(int port, int protocolNumber)
	{
		object policy = null;
		object rules = null;
		bool allowFound = false;
		try
		{
			Type type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2", false);
			if (type == null)
			{
				return false;
			}
			policy = Activator.CreateInstance(type);
			int activeProfiles = Convert.ToInt32(GetComProperty(policy, "CurrentProfileTypes"), CultureInfo.InvariantCulture);
			rules = GetComProperty(policy, "Rules");
			IEnumerable enumerable = rules as IEnumerable;
			if (enumerable == null)
			{
				return false;
			}
			int inspected = 0;
			foreach (object rule in enumerable)
			{
				try
				{
					bool enabled = Convert.ToBoolean(GetComProperty(rule, "Enabled"), CultureInfo.InvariantCulture);
					int direction = Convert.ToInt32(GetComProperty(rule, "Direction"), CultureInfo.InvariantCulture);
					int action = Convert.ToInt32(GetComProperty(rule, "Action"), CultureInfo.InvariantCulture);
					int protocol = Convert.ToInt32(GetComProperty(rule, "Protocol"), CultureInfo.InvariantCulture);
					string localPorts = Convert.ToString(GetComProperty(rule, "LocalPorts"), CultureInfo.InvariantCulture);
					int profiles = Convert.ToInt32(GetComProperty(rule, "Profiles"), CultureInfo.InvariantCulture);
					string application = Convert.ToString(GetComProperty(rule, "ApplicationName"), CultureInfo.InvariantCulture);
					string service = Convert.ToString(GetComProperty(rule, "ServiceName"), CultureInfo.InvariantCulture);
					string localAddresses = Convert.ToString(GetComProperty(rule, "LocalAddresses"), CultureInfo.InvariantCulture);
					string remoteAddresses = Convert.ToString(GetComProperty(rule, "RemoteAddresses"), CultureInfo.InvariantCulture);
					bool applies = enabled && direction == 1 && protocol == protocolNumber && PortListContains(localPorts, port)
						&& (profiles == int.MaxValue || (profiles & activeProfiles) != 0)
						&& string.IsNullOrWhiteSpace(application) && string.IsNullOrWhiteSpace(service)
						&& IsUnrestrictedFirewallAddress(localAddresses) && IsUnrestrictedFirewallAddress(remoteAddresses);
					if (applies && action == 0)
					{
						return false;
					}
					if (applies && action == 1) allowFound = true;
				}
				catch
				{
				}
				finally
				{
					ReleaseComObject(rule);
				}
				inspected++;
				if (inspected >= 10000)
				{
					break;
				}
			}
		}
		catch
		{
		}
		finally
		{
			ReleaseComObject(rules);
			ReleaseComObject(policy);
		}
		return allowFound;
	}

	private static bool IsUnrestrictedFirewallAddress(string value)
	{
		return string.IsNullOrWhiteSpace(value) || string.Equals(value.Trim(), "*", StringComparison.Ordinal);
	}

	private static bool PortListContains(string value, int port)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return false;
		}
		string[] entries = value.Split(',');
		for (int i = 0; i < entries.Length; i++)
		{
			string item = entries[i].Trim();
			if (item == "*")
			{
				return true;
			}
			int dash = item.IndexOf('-');
			int first;
			int last;
			if (dash > 0 && int.TryParse(item.Substring(0, dash), out first) && int.TryParse(item.Substring(dash + 1), out last) && port >= first && port <= last)
			{
				return true;
			}
			if (int.TryParse(item, out first) && first == port)
			{
				return true;
			}
		}
		return false;
	}

	private static bool IsCgnatPossible(string routerExternalIp, string publicIp)
	{
		IPAddress routerAddress;
		IPAddress publicAddress;
		if (!string.IsNullOrWhiteSpace(routerExternalIp) && IPAddress.TryParse(routerExternalIp, out routerAddress))
		{
			if (IsPrivateOrSharedIpv4(routerAddress))
			{
				return true;
			}
			if (!string.IsNullOrWhiteSpace(publicIp) && IPAddress.TryParse(publicIp, out publicAddress) && publicAddress.AddressFamily == routerAddress.AddressFamily && !routerAddress.Equals(publicAddress))
			{
				return true;
			}
		}
		return false;
	}

	private static bool IsPrivateOrSharedIpv4(IPAddress address)
	{
		if (address == null || address.AddressFamily != AddressFamily.InterNetwork)
		{
			return false;
		}
		byte[] bytes = address.GetAddressBytes();
		return bytes[0] == 10
			|| bytes[0] == 127
			|| bytes[0] == 169 && bytes[1] == 254
			|| bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31
			|| bytes[0] == 192 && bytes[1] == 168
			|| bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127;
	}

	private static void ShowManualPortForwardingWindow(string serverDirectory, int serverPort, string javaPath, string publicIp, bool cgnatPossible, bool firewallCheckNeeded, string reason)
	{
		LauncherForm owner = launcherForm;
		if (owner == null || owner.IsDisposed || !owner.IsHandleCreated)
		{
			return;
		}
		TryPostToUi(owner, (MethodInvoker)delegate
		{
			if (owner.IsDisposed)
			{
				return;
			}
			if (activeManualNetworkForm != null && !activeManualNetworkForm.IsDisposed)
			{
				activeManualNetworkForm.Activate();
				return;
			}
			activeManualNetworkForm = new NetworkToolsForm(serverDirectory, serverPort, javaPath, null, publicIp, cgnatPossible, firewallCheckNeeded, reason);
			NetworkToolsForm shown = activeManualNetworkForm;
			shown.FormClosed += delegate
			{
				if (object.ReferenceEquals(activeManualNetworkForm, shown))
				{
					activeManualNetworkForm = null;
				}
			};
			shown.Show(owner);
		});
	}

	private static void ReportExternalAccessStatus(string status, bool warning)
	{
		Console.WriteLine("[외부 접속] " + status);
		string translated = TranslateExternalAccessStatus(status);
		ShowLauncherNotice(translated, warning);
		if (status.EndsWith("중", StringComparison.Ordinal))
		{
			ReportLauncherLoading(translated + "…", -1);
		}
		else
		{
			FinishLauncherLoading();
		}
	}

	private static string TranslateExternalAccessStatus(string status)
	{
		if (!string.Equals(Localization.CurrentLanguage, Localization.English, StringComparison.OrdinalIgnoreCase))
		{
			return status;
		}
		string suffix = string.Empty;
		int separator = status.IndexOf(" · ", StringComparison.Ordinal);
		string key = status;
		if (separator >= 0)
		{
			key = status.Substring(0, separator);
			suffix = status.Substring(separator);
		}
		if (key == "서버 포트 확인 중") return "Checking the local server port" + suffix;
		if (key == "기존 포트포워딩 검사 중") return "Checking existing port forwarding" + suffix;
		if (key == "기존 포트포워딩 정상") return "Existing port forwarding works" + suffix;
		if (key == "외부 접속 실패") return "External access failed" + suffix;
		if (key == "UPnP 장치 검색 중") return "Searching for a UPnP gateway" + suffix;
		if (key == "UPnP 자동 매핑 중") return "Creating a UPnP port mapping" + suffix;
		if (key == "UPnP 매핑 성공") return "UPnP mapping succeeded" + suffix;
		if (key == "UPnP TCP 매핑만 성공") return "Only the UPnP TCP mapping succeeded" + suffix;
		if (key == "UPnP 매핑 실패") return "UPnP mapping failed" + suffix;
		if (key == "외부 접속 재검사 중") return "Rechecking external access" + suffix;
		if (key == "외부 접속 재검사 진행 중") return "An external access recheck is already running" + suffix;
		if (key == "외부 접속 확인 불가") return "External access could not be checked" + suffix;
		if (key == "수동 포트포워딩 필요") return "Manual port forwarding required" + suffix;
		if (key == "CGNAT 가능성 있음") return "CGNAT may be present" + suffix;
		if (key == "포트 충돌 발생") return "Port mapping conflict" + suffix;
		if (key == "포트 매핑 삭제 완료") return "Port mapping removed" + suffix;
		if (key == "포트 매핑 삭제 실패") return "Could not remove the port mapping" + suffix;
		return status;
	}

	private static string FormatExternalAddress(string publicIp, int port)
	{
		return string.IsNullOrWhiteSpace(publicIp) ? string.Empty : (publicIp.IndexOf(':') >= 0 ? "[" + publicIp + "]:" + port : publicIp + ":" + port);
	}

	private static object GetComProperty(object target, string name, params object[] arguments)
	{
		return target.GetType().InvokeMember(name, BindingFlags.GetProperty, null, target, arguments, CultureInfo.InvariantCulture);
	}

	private static object InvokeComMethod(object target, string name, params object[] arguments)
	{
		return target.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, target, arguments, CultureInfo.InvariantCulture);
	}

	private static string SummarizeUpnpError(Exception exception)
	{
		Exception current = exception;
		while (current is TargetInvocationException && current.InnerException != null)
		{
			current = current.InnerException;
		}
		string message = current == null ? string.Empty : current.Message;
		if (string.IsNullOrWhiteSpace(message))
		{
			message = "알 수 없는 UPnP 오류";
		}
		return message.Replace('\r', ' ').Replace('\n', ' ').Trim();
	}

	private static void ReleaseUpnpObjects(UpnpMappingAttempt attempt)
	{
		if (attempt == null)
		{
			return;
		}
		ReleaseComObject(attempt.Collection);
		ReleaseComObject(attempt.NatObject);
		attempt.Collection = null;
		attempt.NatObject = null;
	}

	private static void ReleaseComObject(object value)
	{
		if (value == null || !Marshal.IsComObject(value))
		{
			return;
		}
		try
		{
			Marshal.ReleaseComObject(value);
		}
		catch
		{
		}
	}
}
