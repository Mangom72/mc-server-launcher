using System.IO;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

internal static partial class Launcher
{
    // ---------------------------------------------------------
    // UPnP Network Abstractions (For Mocking)
    // ---------------------------------------------------------
    internal interface INetworkAdapterProvider
    {
        IEnumerable<NetworkInterface> GetActiveAdapters();
    }

    internal interface IUdpClient : IDisposable
    {
        Task<int> SendAsync(byte[] datagram, int bytes, IPEndPoint endPoint);
        Task<UdpReceiveResult> ReceiveAsync();
        void Close();
    }

    internal interface IUdpClientFactory
    {
        IUdpClient Create(IPEndPoint localEP);
    }

    internal interface IHttpClient : IDisposable
    {
        TimeSpan Timeout { get; set; }
        long MaxResponseContentBufferSize { get; set; }
        Task<HttpResponseMessage> GetAsync(string requestUri, CancellationToken cancellationToken);
        Task<HttpResponseMessage> PostAsync(string requestUri, HttpContent content, CancellationToken cancellationToken);
    }

    internal class DefaultNetworkAdapterProvider : INetworkAdapterProvider
    {
        public IEnumerable<NetworkInterface> GetActiveAdapters()
        {
            List<NetworkInterface> validAdapters = new List<NetworkInterface>();
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                if (!nic.SupportsMulticast) continue;

                IPInterfaceProperties props = nic.GetIPProperties();
                if (props.GatewayAddresses.Count == 0) continue;
                
                bool hasIpv4 = false;
                foreach (UnicastIPAddressInformation ip in props.UnicastAddresses)
                {
                    if (ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        hasIpv4 = true;
                        break;
                    }
                }
                if (hasIpv4) validAdapters.Add(nic);
            }
            return validAdapters;
        }
    }

    internal class DefaultUdpClient : IUdpClient
    {
        private readonly UdpClient client;
        public DefaultUdpClient(IPEndPoint localEP) { client = new UdpClient(localEP); }
        public Task<int> SendAsync(byte[] datagram, int bytes, IPEndPoint endPoint) { return client.SendAsync(datagram, bytes, endPoint); }
        public Task<UdpReceiveResult> ReceiveAsync() { return client.ReceiveAsync(); }
        public void Close() { client.Close(); }
        public void Dispose() { ((IDisposable)client).Dispose(); }
    }

    internal class DefaultUdpClientFactory : IUdpClientFactory
    {
        public IUdpClient Create(IPEndPoint localEP) { return new DefaultUdpClient(localEP); }
    }

    internal class DefaultHttpClient : IHttpClient
    {
        private readonly HttpClient client;
        public DefaultHttpClient() 
        { 
            // We use HttpClientHandler to restrict redirects and prevent auto-decompression bombs if needed
            var handler = new HttpClientHandler();
            handler.AllowAutoRedirect = false; // We handle or reject redirects strictly
            client = new HttpClient(handler);
        }
        public TimeSpan Timeout { get { return client.Timeout; } set { client.Timeout = value; } }
        public long MaxResponseContentBufferSize { get { return client.MaxResponseContentBufferSize; } set { client.MaxResponseContentBufferSize = value; } }
        public Task<HttpResponseMessage> GetAsync(string requestUri, CancellationToken cancellationToken) { return client.GetAsync(requestUri, cancellationToken); }
        public Task<HttpResponseMessage> PostAsync(string requestUri, HttpContent content, CancellationToken cancellationToken) { return client.PostAsync(requestUri, content, cancellationToken); }
        public void Dispose() { client.Dispose(); }
    }

    // ---------------------------------------------------------
    // UPnP Interfaces and Models
    // ---------------------------------------------------------
    internal interface IPortMappingService
    {
        Task<UpnpMappingResult> MapPortsAsync(int externalPort, int internalPort, string internalIp, bool needUdp, string description, CancellationToken cancellationToken);
        Task<int> CleanupMappingsAsync(CancellationToken cancellationToken);
    }

    internal class UpnpMappingResult
    {
        public bool Success { get; set; }
        public bool IsConflict { get; set; }
        public bool ExistingMatchingMapping { get; set; }
        public bool IsCgnat { get; set; }
        public string ErrorMessage { get; set; }
        public string RouterIp { get; set; }
        public int MappedTcpCount { get; set; }
        public int MappedUdpCount { get; set; }
    }

    internal class UpnpMappedPort
    {
        public string ProfileId { get; set; }
        public string RouterId { get; set; }
        public string ServiceType { get; set; }
        public string ControlUrl { get; set; }
        public int ExternalPort { get; set; }
        public int InternalPort { get; set; }
        public string InternalIp { get; set; }
        public string Protocol { get; set; }
        public string Description { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    // COM 방식 래퍼 (테스트 통과 후 삭제)
    internal class ComUpnpPortMappingService : IPortMappingService
    {
        public Task<UpnpMappingResult> MapPortsAsync(int externalPort, int internalPort, string internalIp, bool needUdp, string description, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<int> CleanupMappingsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(0);
        }
    }

    // 순수 C# 소켓 방식 (신규)
    internal class SocketUpnpPortMappingService : IPortMappingService
    {
		internal static bool MappingImplemented { get { return false; } }
        private readonly INetworkAdapterProvider _networkProvider;
        private readonly IUdpClientFactory _udpFactory;
        private readonly IHttpClient _httpClient;
        
        public SocketUpnpPortMappingService(INetworkAdapterProvider networkProvider = null, IUdpClientFactory udpFactory = null, IHttpClient httpClient = null)
        {
            _networkProvider = networkProvider ?? new DefaultNetworkAdapterProvider();
            _udpFactory = udpFactory ?? new DefaultUdpClientFactory();
            _httpClient = httpClient ?? new DefaultHttpClient();
        }

        public async Task<UpnpMappingResult> MapPortsAsync(int externalPort, int internalPort, string internalIp, bool needUdp, string description, CancellationToken cancellationToken)
        {
            var result = new UpnpMappingResult();
			if (!MappingImplemented)
			{
				result.ErrorMessage = "소켓 기반 UPnP 매핑은 아직 지원되지 않습니다.";
				return result;
			}
            try
            {
                var locations = await DiscoverDeviceUrlsAsync(TimeSpan.FromSeconds(3), cancellationToken);
                if (locations.Count == 0)
                {
                    result.ErrorMessage = "UPnP 미지원/비활성화 또는 검색 시간 초과";
                    return result;
                }
                
				result.ErrorMessage = "UPnP 장치를 찾았지만 매핑을 만들지 못했습니다.";
            }
            catch (OperationCanceledException)
            {
                result.ErrorMessage = "사용자 취소 또는 전체 제한 시간 초과";
            }
            catch (Exception ex)
            {
                result.ErrorMessage = "네트워크/방화벽 오류: " + ex.Message;
            }
            return result;
        }

        public async Task HandleCrashRecoveryAsync(CancellationToken cancellationToken)
        {
            try
            {
                var mappings = UpnpMappingOwnershipTracker.LoadMappings();
                if (mappings.Count == 0) return;



                Console.WriteLine("[UPnP] 서버 비정상 종료 감지. 이전 UPnP 포트 매핑을 정리합니다.");
                await CleanupMappingsAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[UPnP] 충돌 복구 실패: " + ex.Message);
            }
        }

        public async Task<int> CleanupMappingsAsync(CancellationToken cancellationToken)
        {
            var mappings = UpnpMappingOwnershipTracker.LoadMappings();
            if (mappings.Count == 0) return 0;

            int clearedCount = 0;
            var remaining = new List<UpnpMappedPort>();

            foreach (var m in mappings)
            {
                bool success = false;
                try
                {
					if (!IsSafeStoredMapping(m) || !IsCurrentLocalIpv4(m.InternalIp))
					{
						Console.WriteLine(string.Format("[UPnP] 현재 PC 소유로 확인되지 않아 기록만 폐기합니다: {0} {1}", m.ExternalPort, m.Protocol));
						continue;
					}
					int ownership = await VerifyStoredMappingOwnershipAsync(m, cancellationToken);
					if (ownership == 0)
					{
						Console.WriteLine(string.Format("[UPnP] 공유기의 현재 매핑 소유 정보가 달라 삭제하지 않습니다: {0} {1}", m.ExternalPort, m.Protocol));
						continue;
					}
					if (ownership < 0)
					{
						remaining.Add(m);
						continue;
					}
                    string soapBody = "<?xml version=\"1.0\"?><s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\"><s:Body><u:DeletePortMapping xmlns:u=\"" + m.ServiceType + "\"><NewRemoteHost></NewRemoteHost><NewExternalPort>" + m.ExternalPort + "</NewExternalPort><NewProtocol>" + m.Protocol + "</NewProtocol></u:DeletePortMapping></s:Body></s:Envelope>";
                    using (var httpContent = new StringContent(soapBody, System.Text.Encoding.UTF8, "text/xml"))
                    {
                        httpContent.Headers.Add("SOAPACTION", "\"" + m.ServiceType + "#DeletePortMapping\"");
                        using (var response = await _httpClient.PostAsync(m.ControlUrl, httpContent, cancellationToken))
                        {
                            if (response.IsSuccessStatusCode)
                            {
                                success = true;
                                clearedCount++;
                                Console.WriteLine(string.Format("[UPnP] 매핑 해제 성공: {0} {1}", m.ExternalPort, m.Protocol));
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(string.Format("[UPnP] 매핑 해제 오류: {0} {1} - {2}", m.ExternalPort, m.Protocol, ex.Message));
                }

                if (!success)
                {
                    remaining.Add(m);
                }
            }

            UpnpMappingOwnershipTracker.SaveMappings(remaining);
            return clearedCount;
        }

		private async Task<int> VerifyStoredMappingOwnershipAsync(UpnpMappedPort mapping, CancellationToken cancellationToken)
		{
			string body = "<?xml version=\"1.0\"?><s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\"><s:Body><u:GetSpecificPortMappingEntry xmlns:u=\"" + mapping.ServiceType + "\"><NewRemoteHost></NewRemoteHost><NewExternalPort>" + mapping.ExternalPort + "</NewExternalPort><NewProtocol>" + mapping.Protocol + "</NewProtocol></u:GetSpecificPortMappingEntry></s:Body></s:Envelope>";
			try
			{
				using (var content = new StringContent(body, Encoding.UTF8, "text/xml"))
				{
					content.Headers.Add("SOAPACTION", "\"" + mapping.ServiceType + "#GetSpecificPortMappingEntry\"");
					using (var response = await _httpClient.PostAsync(mapping.ControlUrl, content, cancellationToken))
					{
						if (!response.IsSuccessStatusCode) return 0;
						string xml = await response.Content.ReadAsStringAsync();
						string client = ReadSoapValue(xml, "NewInternalClient");
						string portText = ReadSoapValue(xml, "NewInternalPort");
						string description = ReadSoapValue(xml, "NewPortMappingDescription");
						int port;
						return int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out port)
							&& port == mapping.InternalPort
							&& string.Equals(client, mapping.InternalIp, StringComparison.OrdinalIgnoreCase)
							&& string.Equals(description, mapping.Description, StringComparison.Ordinal) ? 1 : 0;
					}
				}
			}
			catch (OperationCanceledException) { throw; }
			catch (Exception ex)
			{
				Console.WriteLine("[UPnP] 현재 매핑 소유권 확인 실패: " + ex.Message);
				return -1;
			}
		}

		private static string ReadSoapValue(string xml, string element)
		{
			Match match = Regex.Match(xml ?? string.Empty, "<(?:[^>:]+:)?" + element + ">([^<]*)</(?:[^>:]+:)?" + element + ">", RegexOptions.IgnoreCase);
			return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value.Trim()) : string.Empty;
		}

		private static bool IsSafeStoredMapping(UpnpMappedPort mapping)
		{
			Uri control;
			IPAddress address;
			return mapping != null
				&& mapping.ExternalPort >= 1 && mapping.ExternalPort <= 65535
				&& mapping.InternalPort >= 1 && mapping.InternalPort <= 65535
				&& (string.Equals(mapping.Protocol, "TCP", StringComparison.OrdinalIgnoreCase) || string.Equals(mapping.Protocol, "UDP", StringComparison.OrdinalIgnoreCase))
				&& !string.IsNullOrWhiteSpace(mapping.Description) && (mapping.Description.StartsWith("MH-", StringComparison.Ordinal) || mapping.Description.StartsWith("MineHarbor ", StringComparison.Ordinal))
				&& Uri.TryCreate(mapping.ControlUrl, UriKind.Absolute, out control)
				&& string.Equals(control.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
				&& IPAddress.TryParse(control.Host, out address) && IsPrivateOrLinkLocalIpv4(address);
		}

		private static bool IsCurrentLocalIpv4(string value)
		{
			IPAddress expected;
			if (!IPAddress.TryParse(value, out expected) || expected.AddressFamily != AddressFamily.InterNetwork) return false;
			foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
			{
				if (adapter.OperationalStatus != OperationalStatus.Up) continue;
				foreach (UnicastIPAddressInformation address in adapter.GetIPProperties().UnicastAddresses)
				{
					if (expected.Equals(address.Address)) return true;
				}
			}
			return false;
		}

		private static bool IsPrivateOrLinkLocalIpv4(IPAddress address)
		{
			if (address == null || address.AddressFamily != AddressFamily.InterNetwork) return false;
			byte[] bytes = address.GetAddressBytes();
			return bytes[0] == 10 || bytes[0] == 127 || bytes[0] == 169 && bytes[1] == 254 || bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31 || bytes[0] == 192 && bytes[1] == 168;
		}

        
        internal class UpnpServiceInfo
        {
            public string ServiceType { get; set; }
            public string ControlUrl { get; set; }
            public string RouterId { get; set; }
        }

        private async Task<List<UpnpServiceInfo>> FindUpnpServicesAsync(List<string> locations, CancellationToken cancellationToken)
        {
            var services = new List<UpnpServiceInfo>();
            foreach (var loc in locations)
            {
                try
                {
                    var response = await _httpClient.GetAsync(loc, cancellationToken);
                    if (response.IsSuccessStatusCode)
                    {
                        string xml = await response.Content.ReadAsStringAsync();
                        // Basic parsing without XML libraries to avoid DTD issues as requested
                        var matchType = System.Text.RegularExpressions.Regex.Match(xml, @"<serviceType>(urn:schemas-upnp-org:service:WAN(?:IP|PPP)Connection:[1-9])</serviceType>");
                        var matchControl = System.Text.RegularExpressions.Regex.Match(xml, @"<controlURL>([^<]+)</controlURL>");
                        
                        if (matchType.Success && matchControl.Success)
                        {
                            string controlUrl = matchControl.Groups[1].Value;
                            if (!controlUrl.StartsWith("http"))
                            {
                                Uri baseUri = new Uri(loc);
                                controlUrl = new Uri(baseUri, controlUrl).ToString();
                            }
                            services.Add(new UpnpServiceInfo { ServiceType = matchType.Groups[1].Value, ControlUrl = controlUrl, RouterId = "router" });
                        }
                    }
                }
                catch { }
            }
            return services;
        }

        private async Task<bool> AddPortMappingAsync(object serviceObj, int externalPort, int internalPort, string internalIp, string protocol, string description, CancellationToken cancellationToken)
        {
            var service = serviceObj as UpnpServiceInfo;
            if (service == null) return false;

            string safeIp = System.Security.SecurityElement.Escape(internalIp);
            string safeDesc = System.Security.SecurityElement.Escape(description);

            string soapBody = "<?xml version=\"1.0\"?><s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\"><s:Body><u:AddPortMapping xmlns:u=\"" + service.ServiceType + "\"><NewRemoteHost></NewRemoteHost><NewExternalPort>" + externalPort + "</NewExternalPort><NewProtocol>" + protocol + "</NewProtocol><NewInternalPort>" + internalPort + "</NewInternalPort><NewInternalClient>" + safeIp + "</NewInternalClient><NewEnabled>1</NewEnabled><NewPortMappingDescription>" + safeDesc + "</NewPortMappingDescription><NewLeaseDuration>0</NewLeaseDuration></u:AddPortMapping></s:Body></s:Envelope>";

            using (var content = new StringContent(soapBody, System.Text.Encoding.UTF8, "text/xml"))
            {
                content.Headers.Add("SOAPAction", "\"" + service.ServiceType + "#AddPortMapping\"");
                try
                {
                    var response = await _httpClient.PostAsync(service.ControlUrl, content, cancellationToken);
                    return response.IsSuccessStatusCode;
                }
                catch { return false; }
            }
        }


        private async Task<List<string>> DiscoverDeviceUrlsAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            var activeNics = _networkProvider.GetActiveAdapters();
            var searchTasks = new List<Task<List<string>>>();

            foreach (var nic in activeNics)
            {
                var props = nic.GetIPProperties();
                foreach (var ip in props.UnicastAddresses)
                {
                    if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        searchTasks.Add(SearchOnInterfaceAsync(ip.Address, timeout, cancellationToken));
                    }
                }
            }

            var results = await Task.WhenAll(searchTasks);
            var uniqueUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            foreach (var list in results)
            {
                foreach (var url in list)
                {
                    uniqueUrls.Add(url);
                }
            }
            return uniqueUrls.ToList();
        }

        private async Task<List<string>> SearchOnInterfaceAsync(IPAddress localIp, TimeSpan timeout, CancellationToken cancellationToken)
        {
            List<string> locations = new List<string>();
            IUdpClient udpClient = null;
            try
            {
                udpClient = _udpFactory.Create(new IPEndPoint(localIp, 0));
                
                string searchMessage = "M-SEARCH * HTTP/1.1\r\n" +
                                       "HOST: 239.255.255.250:1900\r\n" +
                                       "MAN: \"ssdp:discover\"\r\n" +
                                       "MX: 2\r\n" +
                                       "ST: urn:schemas-upnp-org:device:InternetGatewayDevice:1\r\n\r\n";
                byte[] requestBytes = Encoding.ASCII.GetBytes(searchMessage);
                IPEndPoint multicastEP = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);
                
                await udpClient.SendAsync(requestBytes, requestBytes.Length, multicastEP);

                DateTime endTime = DateTime.UtcNow.Add(timeout);
                while (DateTime.UtcNow < endTime)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    TimeSpan remaining = endTime - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero) break;

                    Task<UdpReceiveResult> receiveTask = udpClient.ReceiveAsync();
                    Task delayTask = Task.Delay(remaining, cancellationToken);
                    
                    Task completed = await Task.WhenAny(receiveTask, delayTask);
                    
                    if (completed == receiveTask)
                    {
                        var result = await receiveTask;
                        string response = Encoding.ASCII.GetString(result.Buffer);
                        string location = ParseLocationHeader(response);
                        if (!string.IsNullOrEmpty(location))
                        {
                            locations.Add(location);
                        }
                    }
                    else
                    {
                        // Timeout or Cancellation.
                        // We must close the UDP client to unblock ReceiveAsync so it can be observed.
                        udpClient.Close();
                        try 
                        { 
                            await receiveTask; // Observe exception
                        } 
                        catch { /* Expected ObjectDisposedException or SocketException */ }
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(string.Format("[UPnP SSDP] {0} 어댑터 검색 실패: {1}", localIp, ex.Message));
            }
            finally
            {
                if (udpClient != null) { udpClient.Dispose(); }
            }
            return locations;
        }

        private string ParseLocationHeader(string response)
        {
            var match = Regex.Match(response, @"^LOCATION:\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Multiline);
            if (match.Success)
            {
                return match.Groups[1].Value.Trim();
            }
            return null;
        }
    }



    internal static class UpnpMappingOwnershipTracker
    {
        private const string TrackerFileName = "launcher-upnp-mappings.tsv";
		internal static string TrackerFilePathOverride { get; set; }

        private static string GetTrackerFilePath()
        {
			if (!string.IsNullOrWhiteSpace(TrackerFilePathOverride)) return TrackerFilePathOverride;
            string dir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string legacy = Path.Combine(dir, "MinecraftServerLauncher");
            string current = Path.Combine(dir, "MineHarbor");
            string targetDir = (!Directory.Exists(current) && Directory.Exists(legacy)) ? legacy : current;
            return Path.Combine(targetDir, TrackerFileName);
        }

        public static List<UpnpMappedPort> LoadMappings()
        {
            var list = new List<UpnpMappedPort>();
            string path = GetTrackerFilePath();
            if (!File.Exists(path)) return list;

            try
            {
                string[] lines = File.ReadAllLines(path, System.Text.Encoding.UTF8);
                foreach (string line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                    string[] parts = line.Split('\t');
                    if (parts.Length >= 10)
                    {
                        var m = new UpnpMappedPort();
                        m.ProfileId = parts[0];
                        m.RouterId = parts[1];
                        m.ServiceType = parts[2];
                        m.ControlUrl = parts[3];
						int ext; int intP;
						if (!int.TryParse(parts[4], NumberStyles.None, CultureInfo.InvariantCulture, out ext) || ext < 1 || ext > 65535) continue;
						if (!int.TryParse(parts[5], NumberStyles.None, CultureInfo.InvariantCulture, out intP) || intP < 1 || intP > 65535) continue;
						m.ExternalPort = ext;
						m.InternalPort = intP;
                        m.InternalIp = parts[6];
						m.Protocol = parts[7].ToUpperInvariant();
						if (m.Protocol != "TCP" && m.Protocol != "UDP") continue;
                        m.Description = parts[8];
						long ticks; if (!long.TryParse(parts[9], NumberStyles.None, CultureInfo.InvariantCulture, out ticks) || ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks) continue;
						m.CreatedAt = new DateTime(ticks, DateTimeKind.Utc);
                        list.Add(m);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[UPnP] 매핑 기록 로드 실패: " + ex.Message);
				throw;
            }
            return list;
        }

        public static void SaveMappings(List<UpnpMappedPort> mappings)
        {
            try
            {
                string path = GetTrackerFilePath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("# ProfileId\tRouterId\tServiceType\tControlUrl\tExternalPort\tInternalPort\tInternalIp\tProtocol\tDescription\tCreatedAt");
                foreach (var m in mappings)
                {
                    sb.AppendLine(string.Format("{0}\t{1}\t{2}\t{3}\t{4}\t{5}\t{6}\t{7}\t{8}\t{9}",
						CleanTrackerField(m.ProfileId), CleanTrackerField(m.RouterId), CleanTrackerField(m.ServiceType), CleanTrackerField(m.ControlUrl), m.ExternalPort, m.InternalPort, CleanTrackerField(m.InternalIp), CleanTrackerField(m.Protocol), CleanTrackerField(m.Description), m.CreatedAt.Ticks));
                }
				string temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
				File.WriteAllText(temporaryPath, sb.ToString(), new UTF8Encoding(false));
				if (File.Exists(path))
				{
					File.Copy(temporaryPath, path, true);
					File.Delete(temporaryPath);
				}
				else File.Move(temporaryPath, path);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[UPnP] 매핑 기록 저장 실패: " + ex.Message);
				throw;
            }
        }

		private static string CleanTrackerField(string value)
		{
			return (value ?? string.Empty).Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();
		}
    }

}
