import re

with open('UpnpExternalAccess.cs', 'r', encoding='utf-8') as f:
    content = f.read()

old_create = r'''\tprivate static UpnpMappingAttempt TryCreateUpnpMappings\(int serverPort, NetworkDetails network, bool udpNeeded, WaitHandle stopped\).*?(?=\n\tprivate static bool TryDiscoverUpnpCollection)'''
new_create = '''\tprivate static UpnpMappingAttempt TryCreateUpnpMappings(int serverPort, NetworkDetails network, bool udpNeeded, WaitHandle stopped)
\t{
\t\tUpnpMappingAttempt result = new UpnpMappingAttempt();
\t\tif (network == null || string.IsNullOrWhiteSpace(network.LocalIpv4))
\t\t{
\t\t\tresult.Error = "현재 PC의 로컬 IPv4 주소를 확인하지 못했습니다.";
\t\t\treturn result;
\t\t}
\t\ttry
\t\t{
\t\t\tif (stopped.WaitOne(0))
\t\t\t{
\t\t\t\tresult.Error = "서버가 종료되어 UPnP 매핑을 중단했습니다.";
\t\t\t\treturn result;
\t\t\t}
\t\t\tif (!TryDiscoverUpnpCollection(result, stopped))
\t\t\t{
\t\t\t\treturn result;
\t\t\t}
\t\t\tReportExternalAccessStatus("UPnP 자동 매핑 중", false);
\t\t\tstring token = Guid.NewGuid().ToString("N").Substring(0, 12);
\t\t\tstring description = "MineHarbor " + token;
\t\t\tif (!TryAddSingleUpnpMapping(result, serverPort, serverPort, "TCP", network.LocalIpv4, description, stopped))
\t\t\t{
\t\t\t\treturn result;
\t\t\t}
\t\t\tif (udpNeeded)
\t\t\t{
\t\t\t\tif (!TryAddSingleUpnpMapping(result, serverPort, serverPort, "UDP", network.LocalIpv4, description, stopped))
\t\t\t\t{
\t\t\t\t\tif (result.PortConflict)
\t\t\t\t\t{
\t\t\t\t\t\tReportExternalAccessStatus("포트 충돌 발생", true);
\t\t\t\t\t\tresult.PortConflict = false;
\t\t\t\t\t}
\t\t\t\t\tConsole.WriteLine("[UPnP] UDP 매핑을 만들지 못했습니다. TCP 외부 접속은 계속 검사합니다.");
\t\t\t\t}
\t\t\t}
\t\t}
\t\tcatch (Exception exception)
\t\t{
\t\t\tresult.Error = SummarizeUpnpError(exception);
\t\t}
\t\treturn result;
\t}
'''

old_delete = r'''\tprivate static void DeleteCreatedUpnpMappings\(UpnpMappingAttempt attempt\).*?(?=\n\tinternal static int ClearAllMineHarborUpnpMappings)'''
new_delete = '''\tprivate static void DeleteCreatedUpnpMappings(UpnpMappingAttempt attempt)
\t{
\t\tbool allDeleted = true;
\t\tfor (int i = attempt.Created.Count - 1; i >= 0; i--)
\t\t{
\t\t\tCreatedUpnpMapping record = attempt.Created[i];
\t\t\tobject current = FindUpnpMapping(attempt.Collection, record.ExternalPort, record.Protocol);
\t\t\tif (current == null)
\t\t\t{
\t\t\t\tcontinue;
\t\t\t}
\t\t\ttry
\t\t\t{
\t\t\t\tstring client = Convert.ToString(GetComProperty(current, "InternalClient"), CultureInfo.InvariantCulture);
\t\t\t\tint port = Convert.ToInt32(GetComProperty(current, "InternalPort"), CultureInfo.InvariantCulture);
\t\t\t\tstring description = Convert.ToString(GetComProperty(current, "Description"), CultureInfo.InvariantCulture);
\t\t\t\tif (!string.Equals(client, record.InternalClient, StringComparison.OrdinalIgnoreCase) || port != record.InternalPort || !string.Equals(description, record.Description, StringComparison.Ordinal))
\t\t\t\t{
\t\t\t\t\tallDeleted = false;
\t\t\t\t\tConsole.WriteLine("[UPnP] 매핑 소유 정보가 달라 삭제하지 않았습니다: " + record.Protocol + " " + record.ExternalPort);
\t\t\t\t\tcontinue;
\t\t\t\t}
\t\t\t\tInvokeComMethod(attempt.Collection, "Remove", record.ExternalPort, record.Protocol);
\t\t\t}
\t\t\tcatch (Exception exception)
\t\t\t{
\t\t\t\tallDeleted = false;
\t\t\t\tConsole.WriteLine("[UPnP] 매핑 삭제 실패: " + SummarizeUpnpError(exception));
\t\t\t}
\t\t\tfinally
\t\t\t{
\t\t\t\tReleaseComObject(current);
\t\t\t}
\t\t}
\t\tstring status = allDeleted ? "포트 매핑 삭제 완료" : "포트 매핑 삭제 실패";
\t\tInterlocked.Exchange(ref lastUpnpCleanupStatus, TranslateExternalAccessStatus(status));
\t\tReportExternalAccessStatus(status, !allDeleted);
\t}
'''

if re.search(old_create, content, re.DOTALL):
    content = re.sub(old_create, new_create, content, flags=re.DOTALL)
    print("Fix 1 applied (TryCreateUpnpMappings).")
else:
    print("Failed to find old_create")

if re.search(old_delete, content, re.DOTALL):
    content = re.sub(old_delete, new_delete, content, flags=re.DOTALL)
    print("Fix 2 applied (DeleteCreatedUpnpMappings).")
else:
    print("Failed to find old_delete")

with open('UpnpExternalAccess.cs', 'w', encoding='utf-8') as f:
    f.write(content)
print("UpnpExternalAccess.cs updated.")