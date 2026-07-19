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
    // 시험에서 실제 네트워크를 대체하기 위한 UPnP 추상화
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
            // 공유기 제어 주소가 다른 호스트로 바뀌지 않도록 자동 리디렉션을 차단합니다.
            var handler = new HttpClientHandler();
            handler.AllowAutoRedirect = false;
            client = new HttpClient(handler);
        }
        public TimeSpan Timeout { get { return client.Timeout; } set { client.Timeout = value; } }
        public long MaxResponseContentBufferSize { get { return client.MaxResponseContentBufferSize; } set { client.MaxResponseContentBufferSize = value; } }
        public Task<HttpResponseMessage> GetAsync(string requestUri, CancellationToken cancellationToken) { return client.GetAsync(requestUri, cancellationToken); }
        public Task<HttpResponseMessage> PostAsync(string requestUri, HttpContent content, CancellationToken cancellationToken) { return client.PostAsync(requestUri, content, cancellationToken); }
        public void Dispose() { client.Dispose(); }
    }

    // ---------------------------------------------------------
    // UPnP 인터페이스와 상태 모델
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
        public int ExternalPort { get; set; }
        public int MappedTcpCount { get; set; }
        public int MappedUdpCount { get; set; }
        public List<UpnpMappedPort> CreatedMappings { get; private set; }

        public UpnpMappingResult()
        {
            CreatedMappings = new List<UpnpMappedPort>();
        }
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

    // Windows COM이 동작하지 않는 환경에서도 사용할 수 있는 SSDP/SOAP 방식입니다.
    internal class SocketUpnpPortMappingService : IPortMappingService
    {
        private sealed class SoapMappingInfo
        {
            public int InternalPort;
            public string InternalClient;
            public string Description;
        }

        private sealed class ProtocolMappingResult
        {
            public bool Usable;
            public bool Created;
            public bool Existing;
            public bool Conflict;
            public string Error;
            public UpnpMappedPort Record;
        }

        private sealed class DiscoveredDeviceUrl
        {
            public string Location;
            public string LocalIp;
        }

        internal static bool MappingImplemented { get { return true; } }
        private readonly INetworkAdapterProvider _networkProvider;
        private readonly IUdpClientFactory _udpFactory;
        private readonly IHttpClient _httpClient;
        
        public SocketUpnpPortMappingService(INetworkAdapterProvider networkProvider = null, IUdpClientFactory udpFactory = null, IHttpClient httpClient = null)
        {
            _networkProvider = networkProvider ?? new DefaultNetworkAdapterProvider();
            _udpFactory = udpFactory ?? new DefaultUdpClientFactory();
            _httpClient = httpClient ?? new DefaultHttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(8);
            _httpClient.MaxResponseContentBufferSize = 1024 * 1024;
        }

        public async Task<UpnpMappingResult> MapPortsAsync(int externalPort, int internalPort, string internalIp, bool needUdp, string description, CancellationToken cancellationToken)
        {
            return await MapPortsWithFallbackAsync(new int[] { externalPort }, internalPort, internalIp, needUdp, description, cancellationToken).ConfigureAwait(false);
        }

        internal async Task<UpnpMappingResult> MapPortsWithFallbackAsync(IList<int> externalPorts, int internalPort, string internalIp, bool needUdp, string description, CancellationToken cancellationToken)
        {
            var result = new UpnpMappingResult();
            result.ExternalPort = externalPorts == null || externalPorts.Count == 0 ? internalPort : externalPorts[0];
            try
            {
                var locations = await DiscoverDeviceUrlsAsync(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
                if (locations.Count == 0)
                {
                    result.ErrorMessage = "UPnP 미지원/비활성화 또는 검색 시간 초과";
                    return result;
                }

                List<UpnpServiceInfo> services = await FindUpnpServicesAsync(locations, cancellationToken).ConfigureAwait(false);
                if (services.Count == 0)
                {
                    result.ErrorMessage = "UPnP 장치는 응답했지만 WAN 포트 매핑 서비스를 찾지 못했습니다.";
                    return result;
                }

                string lastError = null;
                for (int i = 0; i < services.Count; i++)
                {
                    UpnpMappingResult current = await MapPortsOnServiceWithFallbackAsync(services[i], externalPorts, internalPort, internalIp, needUdp, description, cancellationToken).ConfigureAwait(false);
                    if (current.Success || current.MappedTcpCount > 0 || current.ExistingMatchingMapping)
                    {
                        return current;
                    }
                    lastError = current.ErrorMessage;
                }
                result.ErrorMessage = string.IsNullOrWhiteSpace(lastError) ? "UPnP 장치를 찾았지만 매핑을 만들지 못했습니다." : lastError;
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

        internal async Task<UpnpMappingResult> MapPortsOnServiceWithFallbackAsync(UpnpServiceInfo service, IList<int> externalPorts, int internalPort, string internalIp, bool needUdp, string description, CancellationToken cancellationToken)
        {
            UpnpMappingResult result = new UpnpMappingResult();
            result.ExternalPort = externalPorts == null || externalPorts.Count == 0 ? internalPort : externalPorts[0];
            string mappedInternalIp = service != null && !string.IsNullOrWhiteSpace(service.LocalIp) ? service.LocalIp : internalIp;
            string lastError = null;
            for (int portIndex = 0; externalPorts != null && portIndex < externalPorts.Count; portIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                UpnpMappingResult current = await MapPortsOnServiceAsync(service, externalPorts[portIndex], internalPort, mappedInternalIp, needUdp, description, cancellationToken).ConfigureAwait(false);
                if (current.Success || current.MappedTcpCount > 0 || current.ExistingMatchingMapping) return current;
                lastError = current.ErrorMessage;
                result.IsConflict = result.IsConflict || current.IsConflict;
                if (!current.IsConflict) break;
            }
            result.ErrorMessage = string.IsNullOrWhiteSpace(lastError) ? "지정한 외부 포트 후보에서 매핑을 만들지 못했습니다." : lastError;
            return result;
        }

        internal async Task<UpnpMappingResult> MapPortsOnServiceAsync(UpnpServiceInfo service, int externalPort, int internalPort, string internalIp, bool needUdp, string description, CancellationToken cancellationToken)
        {
            UpnpMappingResult result = new UpnpMappingResult();
            result.ExternalPort = externalPort;
            if (service == null || !IsSafeRouterUrl(service.ControlUrl))
            {
                result.ErrorMessage = "안전한 공유기 제어 주소를 확인하지 못했습니다.";
                return result;
            }
            Uri controlUri = new Uri(service.ControlUrl);
            result.RouterIp = controlUri.Host;

            ProtocolMappingResult tcp = await EnsureProtocolMappingAsync(service, externalPort, internalPort, internalIp, "TCP", description, cancellationToken).ConfigureAwait(false);
            if (!tcp.Usable)
            {
                result.IsConflict = tcp.Conflict;
                result.ErrorMessage = tcp.Error;
                return result;
            }
            result.MappedTcpCount = tcp.Created ? 1 : 0;
            result.ExistingMatchingMapping = tcp.Existing;
            if (tcp.Record != null) result.CreatedMappings.Add(tcp.Record);

            if (needUdp)
            {
                ProtocolMappingResult udp = await EnsureProtocolMappingAsync(service, externalPort, internalPort, internalIp, "UDP", description, cancellationToken).ConfigureAwait(false);
                if (udp.Usable)
                {
                    result.MappedUdpCount = udp.Created ? 1 : 0;
                    result.ExistingMatchingMapping = result.ExistingMatchingMapping || udp.Existing;
                    if (udp.Record != null) result.CreatedMappings.Add(udp.Record);
                }
                else
                {
                    result.IsConflict = udp.Conflict;
                    result.ErrorMessage = "TCP 매핑은 사용할 수 있지만 UDP 매핑에 실패했습니다. " + (udp.Error ?? string.Empty);
                    result.Success = false;
                    return result;
                }
            }

            result.Success = true;
            return result;
        }

        private async Task<ProtocolMappingResult> EnsureProtocolMappingAsync(UpnpServiceInfo service, int externalPort, int internalPort, string internalIp, string protocol, string description, CancellationToken cancellationToken)
        {
            ProtocolMappingResult result = new ProtocolMappingResult();
            SoapMappingInfo existing = await GetSpecificMappingAsync(service, externalPort, protocol, cancellationToken).ConfigureAwait(false);
            if (existing != null)
            {
                if (existing.InternalPort == internalPort && string.Equals(existing.InternalClient, internalIp, StringComparison.OrdinalIgnoreCase))
                {
                    result.Usable = true;
                    result.Existing = true;
                    if (string.Equals(existing.Description, description, StringComparison.Ordinal))
                    {
                        result.Created = true;
                        result.Existing = false;
                        result.Record = CreateTrackedRecord(service, externalPort, internalPort, internalIp, protocol, description);
                        PersistTrackedUpnpMapping(externalPort, internalPort, protocol, internalIp, description, service.ServiceType, service.ControlUrl);
                    }
                    else if (TryAdoptTrackedUpnpMapping(externalPort, internalPort, protocol, internalIp, existing.Description, service.ServiceType, service.ControlUrl))
                    {
                        result.Created = true;
                        result.Existing = false;
                        result.Record = CreateTrackedRecord(service, externalPort, internalPort, internalIp, protocol, existing.Description);
                    }
                    return result;
                }
                result.Conflict = true;
                result.Error = protocol + " " + externalPort.ToString(CultureInfo.InvariantCulture) + " 포트가 이미 다른 내부 대상에 연결되어 있습니다.";
                return result;
            }

            UpnpMappedPort pending = CreateTrackedRecord(service, externalPort, internalPort, internalIp, protocol, description);
            PersistTrackedUpnpMapping(externalPort, internalPort, protocol, internalIp, description, service.ServiceType, service.ControlUrl);
            bool addCompleted = false;
            string addError = null;
            try
            {
                addCompleted = await SendAddPortMappingAsync(service, pending, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                MarkTrackedOwnershipInactive(description, protocol);
                throw;
            }
            catch (Exception ex)
            {
                addError = ex.Message;
            }

            SoapMappingInfo verified = null;
            try
            {
                verified = await GetSpecificMappingAsync(service, externalPort, protocol, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                MarkTrackedOwnershipInactive(description, protocol);
                throw;
            }
            catch (Exception ex)
            {
                if (string.IsNullOrWhiteSpace(addError)) addError = ex.Message;
            }

            if (verified != null && verified.InternalPort == internalPort && string.Equals(verified.InternalClient, internalIp, StringComparison.OrdinalIgnoreCase) && string.Equals(verified.Description, description, StringComparison.Ordinal))
            {
                result.Usable = true;
                result.Created = true;
                result.Record = pending;
                return result;
            }
            if (verified != null)
            {
                RemoveTrackedSocketMapping(pending);
                result.Conflict = true;
                result.Error = protocol + " " + externalPort.ToString(CultureInfo.InvariantCulture) + " 포트의 현재 소유 정보가 다릅니다.";
                return result;
            }
            if (addCompleted)
            {
                // 공유기가 성공 응답 뒤 목록 반영을 늦추는 경우 종료 시 다시 소유권을 확인하도록 기록을 유지합니다.
                result.Usable = true;
                result.Created = true;
                result.Record = pending;
                return result;
            }

            RemoveTrackedSocketMapping(pending);
            result.Error = string.IsNullOrWhiteSpace(addError) ? protocol + " UPnP 매핑 생성에 실패했습니다." : addError;
            return result;
        }

        private async Task<bool> SendAddPortMappingAsync(UpnpServiceInfo service, UpnpMappedPort mapping, CancellationToken cancellationToken)
        {
            string safeIp = System.Security.SecurityElement.Escape(mapping.InternalIp);
            string safeDesc = System.Security.SecurityElement.Escape(mapping.Description);
            string body = "<?xml version=\"1.0\"?><s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\"><s:Body><u:AddPortMapping xmlns:u=\"" + service.ServiceType + "\"><NewRemoteHost></NewRemoteHost><NewExternalPort>" + mapping.ExternalPort.ToString(CultureInfo.InvariantCulture) + "</NewExternalPort><NewProtocol>" + mapping.Protocol + "</NewProtocol><NewInternalPort>" + mapping.InternalPort.ToString(CultureInfo.InvariantCulture) + "</NewInternalPort><NewInternalClient>" + safeIp + "</NewInternalClient><NewEnabled>1</NewEnabled><NewPortMappingDescription>" + safeDesc + "</NewPortMappingDescription><NewLeaseDuration>0</NewLeaseDuration></u:AddPortMapping></s:Body></s:Envelope>";
            using (StringContent content = new StringContent(body, Encoding.UTF8, "text/xml"))
            {
                content.Headers.Add("SOAPAction", "\"" + service.ServiceType + "#AddPortMapping\"");
                using (HttpResponseMessage response = await _httpClient.PostAsync(service.ControlUrl, content, cancellationToken).ConfigureAwait(false))
                {
                    if (response.IsSuccessStatusCode) return true;
                    string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    string errorCode = ReadSoapValue(responseBody, "errorCode");
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(errorCode) ? "공유기가 AddPortMapping 요청을 거부했습니다." : "공유기가 AddPortMapping 요청을 거부했습니다. 오류 코드 " + errorCode);
                }
            }
        }

        private async Task<SoapMappingInfo> GetSpecificMappingAsync(UpnpServiceInfo service, int externalPort, string protocol, CancellationToken cancellationToken)
        {
            string body = "<?xml version=\"1.0\"?><s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\"><s:Body><u:GetSpecificPortMappingEntry xmlns:u=\"" + service.ServiceType + "\"><NewRemoteHost></NewRemoteHost><NewExternalPort>" + externalPort.ToString(CultureInfo.InvariantCulture) + "</NewExternalPort><NewProtocol>" + protocol + "</NewProtocol></u:GetSpecificPortMappingEntry></s:Body></s:Envelope>";
            using (StringContent content = new StringContent(body, Encoding.UTF8, "text/xml"))
            {
                content.Headers.Add("SOAPAction", "\"" + service.ServiceType + "#GetSpecificPortMappingEntry\"");
                using (HttpResponseMessage response = await _httpClient.PostAsync(service.ControlUrl, content, cancellationToken).ConfigureAwait(false))
                {
                    string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        string code = ReadSoapValue(responseBody, "errorCode");
                        if (string.Equals(code, "714", StringComparison.Ordinal)) return null;
                        throw new InvalidOperationException(string.IsNullOrWhiteSpace(code) ? "현재 포트 매핑을 조회하지 못했습니다." : "현재 포트 매핑을 조회하지 못했습니다. 오류 코드 " + code);
                    }
                    int internalPort;
                    if (!int.TryParse(ReadSoapValue(responseBody, "NewInternalPort"), NumberStyles.None, CultureInfo.InvariantCulture, out internalPort))
                    {
                        throw new InvalidDataException("공유기의 포트 매핑 응답에서 내부 포트를 확인하지 못했습니다.");
                    }
                    SoapMappingInfo mapping = new SoapMappingInfo();
                    mapping.InternalPort = internalPort;
                    mapping.InternalClient = ReadSoapValue(responseBody, "NewInternalClient");
                    mapping.Description = ReadSoapValue(responseBody, "NewPortMappingDescription");
                    return mapping;
                }
            }
        }

        private static UpnpMappedPort CreateTrackedRecord(UpnpServiceInfo service, int externalPort, int internalPort, string internalIp, string protocol, string description)
        {
            UpnpMappedPort record = new UpnpMappedPort();
            record.RouterId = service.RouterId;
            record.ServiceType = service.ServiceType;
            record.ControlUrl = service.ControlUrl;
            record.ExternalPort = externalPort;
            record.InternalPort = internalPort;
            record.InternalIp = internalIp;
            record.Protocol = protocol;
            record.Description = description;
            record.CreatedAt = DateTime.UtcNow;
            return record;
        }

        private static void RemoveTrackedSocketMapping(UpnpMappedPort mapping)
        {
            if (mapping == null) return;
            CreatedUpnpMapping created = new CreatedUpnpMapping();
            created.ExternalPort = mapping.ExternalPort;
            created.InternalPort = mapping.InternalPort;
            created.InternalClient = mapping.InternalIp;
            created.Protocol = mapping.Protocol;
            created.Description = mapping.Description;
            RemoveTrackedUpnpMapping(created);
            MarkTrackedOwnershipInactive(mapping.Description, mapping.Protocol);
        }

        public async Task HandleCrashRecoveryAsync(CancellationToken cancellationToken)
        {
            try
            {
                var mappings = UpnpMappingOwnershipTracker.LoadMappings();
                if (mappings.Count == 0) return;



                Console.WriteLine("[UPnP] 서버 비정상 종료 감지. 이전 UPnP 포트 매핑을 정리합니다.");
                await CleanupMappingsAsync(cancellationToken).ConfigureAwait(false);
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
            List<UpnpMappedPort> stale = new List<UpnpMappedPort>();
            for (int i = 0; i < mappings.Count; i++)
            {
                if (IsSafeStoredMapping(mappings[i]) && !IsTrackedOwnerAlive(mappings[i])) stale.Add(mappings[i]);
            }
            return await DeleteMappingsAsync(stale, cancellationToken).ConfigureAwait(false);
        }

        internal async Task<int> DeleteMappingsAsync(IList<UpnpMappedPort> mappings, CancellationToken cancellationToken)
        {
            int clearedCount = 0;
            if (mappings == null) return clearedCount;
            for (int i = 0; i < mappings.Count; i++)
            {
                UpnpMappedPort mapping = mappings[i];
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsSafeStoredMapping(mapping) || !IsCurrentLocalIpv4(mapping.InternalIp))
                {
                    Console.WriteLine(string.Format("[UPnP] 현재 PC 소유로 확인되지 않아 기록만 폐기합니다: {0} {1}", mapping == null ? 0 : mapping.ExternalPort, mapping == null ? string.Empty : mapping.Protocol));
                    RemoveTrackedSocketMapping(mapping);
                    continue;
                }
                try
                {
                    UpnpServiceInfo service = new UpnpServiceInfo();
                    service.ServiceType = mapping.ServiceType;
                    service.ControlUrl = mapping.ControlUrl;
                    service.RouterId = mapping.RouterId;
                    SoapMappingInfo current = await GetSpecificMappingAsync(service, mapping.ExternalPort, mapping.Protocol, cancellationToken).ConfigureAwait(false);
                    if (current == null)
                    {
                        RemoveTrackedSocketMapping(mapping);
                        clearedCount++;
                        continue;
                    }
                    if (current.InternalPort != mapping.InternalPort || !string.Equals(current.InternalClient, mapping.InternalIp, StringComparison.OrdinalIgnoreCase) || !string.Equals(current.Description, mapping.Description, StringComparison.Ordinal))
                    {
                        Console.WriteLine(string.Format("[UPnP] 공유기의 현재 매핑 소유 정보가 달라 삭제하지 않습니다: {0} {1}", mapping.ExternalPort, mapping.Protocol));
                        RemoveTrackedSocketMapping(mapping);
                        clearedCount++;
                        continue;
                    }
                    if (await SendDeletePortMappingAsync(service, mapping.ExternalPort, mapping.Protocol, cancellationToken).ConfigureAwait(false))
                    {
                        clearedCount++;
                        RemoveTrackedSocketMapping(mapping);
                        Console.WriteLine(string.Format("[UPnP] 매핑 해제 성공: {0} {1}", mapping.ExternalPort, mapping.Protocol));
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Console.WriteLine(string.Format("[UPnP] 매핑 해제 오류: {0} {1} - {2}", mapping.ExternalPort, mapping.Protocol, ex.Message));
                }
            }
            return clearedCount;
        }

        private async Task<bool> SendDeletePortMappingAsync(UpnpServiceInfo service, int externalPort, string protocol, CancellationToken cancellationToken)
        {
            string body = "<?xml version=\"1.0\"?><s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\"><s:Body><u:DeletePortMapping xmlns:u=\"" + service.ServiceType + "\"><NewRemoteHost></NewRemoteHost><NewExternalPort>" + externalPort.ToString(CultureInfo.InvariantCulture) + "</NewExternalPort><NewProtocol>" + protocol + "</NewProtocol></u:DeletePortMapping></s:Body></s:Envelope>";
            using (StringContent content = new StringContent(body, Encoding.UTF8, "text/xml"))
            {
                content.Headers.Add("SOAPAction", "\"" + service.ServiceType + "#DeletePortMapping\"");
                using (HttpResponseMessage response = await _httpClient.PostAsync(service.ControlUrl, content, cancellationToken).ConfigureAwait(false))
                {
                    if (response.IsSuccessStatusCode) return true;
                    string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    string code = ReadSoapValue(responseBody, "errorCode");
                    if (string.Equals(code, "714", StringComparison.Ordinal)) return true;
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(code) ? "공유기가 DeletePortMapping 요청을 거부했습니다." : "공유기가 DeletePortMapping 요청을 거부했습니다. 오류 코드 " + code);
                }
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
            public string LocalIp { get; set; }
        }

        private async Task<List<UpnpServiceInfo>> FindUpnpServicesAsync(List<DiscoveredDeviceUrl> locations, CancellationToken cancellationToken)
        {
            var services = new List<UpnpServiceInfo>();
            foreach (DiscoveredDeviceUrl device in locations)
            {
                string loc = device.Location;
                if (!IsSafeRouterUrl(loc)) continue;
                try
                {
                    using (HttpResponseMessage response = await _httpClient.GetAsync(loc, cancellationToken).ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode) continue;
                        string xml = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        List<UpnpServiceInfo> parsed = ParseUpnpServices(xml, new Uri(loc));
                        for (int i = 0; i < parsed.Count; i++) parsed[i].LocalIp = device.LocalIp;
                        services.AddRange(parsed);
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { Console.WriteLine("[UPnP] 장치 설명 확인 실패: " + ex.Message); }
            }
            services.Sort(delegate(UpnpServiceInfo left, UpnpServiceInfo right)
            {
                bool leftIp = left.ServiceType.IndexOf("WANIPConnection", StringComparison.OrdinalIgnoreCase) >= 0;
                bool rightIp = right.ServiceType.IndexOf("WANIPConnection", StringComparison.OrdinalIgnoreCase) >= 0;
                if (leftIp != rightIp) return leftIp ? -1 : 1;
                return string.Compare(right.ServiceType, left.ServiceType, StringComparison.OrdinalIgnoreCase);
            });
            return services;
        }

        internal static List<UpnpServiceInfo> ParseUpnpServices(string xml, Uri descriptionUri)
        {
            List<UpnpServiceInfo> services = new List<UpnpServiceInfo>();
            if (descriptionUri == null || string.IsNullOrWhiteSpace(xml) || xml.Length > 1024 * 1024) return services;
            MatchCollection blocks = Regex.Matches(xml, @"<(?:[A-Za-z0-9_.-]+:)?service\b[^>]*>(.*?)</(?:[A-Za-z0-9_.-]+:)?service\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            for (int i = 0; i < blocks.Count && services.Count < 16; i++)
            {
                string block = blocks[i].Groups[1].Value;
                string serviceType = ReadXmlText(block, "serviceType");
                if (!Regex.IsMatch(serviceType ?? string.Empty, @"^urn:schemas-upnp-org:service:WAN(?:IP|PPP)Connection:[1-9][0-9]*$", RegexOptions.IgnoreCase)) continue;
                string controlText = ReadXmlText(block, "controlURL");
                Uri controlUri;
                if (string.IsNullOrWhiteSpace(controlText) || !Uri.TryCreate(descriptionUri, controlText, out controlUri) || !IsSafeRouterUrl(controlUri.ToString()) || !IsSameRouterHost(descriptionUri, controlUri)) continue;
                bool duplicate = false;
                for (int j = 0; j < services.Count; j++)
                {
                    if (string.Equals(services[j].ServiceType, serviceType, StringComparison.OrdinalIgnoreCase) && string.Equals(services[j].ControlUrl, controlUri.ToString(), StringComparison.OrdinalIgnoreCase))
                    {
                        duplicate = true;
                        break;
                    }
                }
                if (duplicate) continue;
                UpnpServiceInfo service = new UpnpServiceInfo();
                service.ServiceType = serviceType;
                service.ControlUrl = controlUri.ToString();
                service.RouterId = descriptionUri.Host;
                services.Add(service);
            }
            return services;
        }

        private static string ReadXmlText(string xml, string element)
        {
            Match match = Regex.Match(xml ?? string.Empty, "<(?:[A-Za-z0-9_.-]+:)?" + Regex.Escape(element) + @"\b[^>]*>(.*?)</(?:[A-Za-z0-9_.-]+:)?" + Regex.Escape(element) + @"\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            return match.Success ? WebUtility.HtmlDecode(Regex.Replace(match.Groups[1].Value, @"\s+", " ").Trim()) : string.Empty;
        }

        private static bool IsSafeRouterUrl(string value)
        {
            Uri uri;
            IPAddress address;
            return Uri.TryCreate(value, UriKind.Absolute, out uri)
                && string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrEmpty(uri.UserInfo)
                && IPAddress.TryParse(uri.Host, out address)
                && IsPrivateOrLinkLocalIpv4(address);
        }

        private static bool IsSameRouterHost(Uri first, Uri second)
        {
            IPAddress firstAddress;
            IPAddress secondAddress;
            return first != null && second != null
                && IPAddress.TryParse(first.Host, out firstAddress)
                && IPAddress.TryParse(second.Host, out secondAddress)
                && firstAddress.Equals(secondAddress);
        }


        private async Task<List<DiscoveredDeviceUrl>> DiscoverDeviceUrlsAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            var activeNics = _networkProvider.GetActiveAdapters();
            var searchTasks = new List<Task<List<DiscoveredDeviceUrl>>>();

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

            var results = await Task.WhenAll(searchTasks).ConfigureAwait(false);
            var uniqueUrls = new Dictionary<string, DiscoveredDeviceUrl>(StringComparer.OrdinalIgnoreCase);
            
            foreach (var list in results)
            {
                foreach (DiscoveredDeviceUrl device in list)
                {
                    if (!uniqueUrls.ContainsKey(device.Location)) uniqueUrls.Add(device.Location, device);
                    if (uniqueUrls.Count >= 64) return uniqueUrls.Values.ToList();
                }
            }
            return uniqueUrls.Values.ToList();
        }

        private async Task<List<DiscoveredDeviceUrl>> SearchOnInterfaceAsync(IPAddress localIp, TimeSpan timeout, CancellationToken cancellationToken)
        {
            List<DiscoveredDeviceUrl> locations = new List<DiscoveredDeviceUrl>();
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
                
                await udpClient.SendAsync(requestBytes, requestBytes.Length, multicastEP).ConfigureAwait(false);

                DateTime endTime = DateTime.UtcNow.Add(timeout);
                while (DateTime.UtcNow < endTime)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    TimeSpan remaining = endTime - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero) break;

                    Task<UdpReceiveResult> receiveTask = udpClient.ReceiveAsync();
                    Task delayTask = Task.Delay(remaining, cancellationToken);
                    
                    Task completed = await Task.WhenAny(receiveTask, delayTask).ConfigureAwait(false);
                    
                    if (completed == receiveTask)
                    {
                        var result = await receiveTask.ConfigureAwait(false);
                        string response = Encoding.ASCII.GetString(result.Buffer);
                        string location = ParseLocationHeader(response, result.RemoteEndPoint == null ? null : result.RemoteEndPoint.Address);
                        if (!string.IsNullOrEmpty(location))
                        {
                            locations.Add(new DiscoveredDeviceUrl { Location = location, LocalIp = localIp.ToString() });
                            if (locations.Count >= 32) break;
                        }
                    }
                    else
                    {
                        // 제한 시간이나 취소 시 ReceiveAsync를 깨우기 위해 UDP 소켓을 닫습니다.
                        udpClient.Close();
                        try 
                        { 
                            await receiveTask.ConfigureAwait(false); // 종료를 위해 닫은 소켓의 예외를 관찰합니다.
                        } 
                        catch { /* 소켓을 닫을 때 발생하는 정상적인 종료 예외입니다. */ }
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

        private string ParseLocationHeader(string response, IPAddress responseAddress)
        {
            if (string.IsNullOrEmpty(response) || response.Length > 65536) return null;
            var match = Regex.Match(response, @"^LOCATION:\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Multiline);
            if (match.Success)
            {
                string location = match.Groups[1].Value.Trim();
                Uri locationUri;
                IPAddress locationAddress;
                return IsSafeRouterUrl(location)
                    && Uri.TryCreate(location, UriKind.Absolute, out locationUri)
                    && IPAddress.TryParse(locationUri.Host, out locationAddress)
                    && (responseAddress == null || responseAddress.Equals(locationAddress)) ? location : null;
            }
            return null;
        }

        public void Dispose()
        {
            _httpClient.Dispose();
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
