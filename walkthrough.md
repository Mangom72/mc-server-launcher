# UPnP C# 순수 소켓 방식 리팩토링 및 테스트 완료

UPnP 포트 포워딩을 기존 COM 기반 방식에서 순수 C# UdpClient 및 HttpClient 기반으로 리팩토링하는 작업을 완료했습니다.

## 주요 변경 사항

### 1. SocketUpnpPortMappingService 완벽 구현 (UpnpCore.cs)
- **SSDP 멀티캐스트 검색**: UdpClient를 이용해 백그라운드 스레드에서 안전하게 로컬 네트워크의 UPnP 장치를 검색합니다. 사용자 취소 및 전체 타임아웃 처리가 CancellationToken과 Task.WhenAny를 통해 견고하게 구현되었습니다.
- **XML 파싱 보안**: DTD 금지(DtdProcessing.Prohibit), XmlResolver = null, 제한된 응답 크기(MaxResponseContentBufferSize) 등 안전한 XML 처리 환경을 구축했습니다.
- **포트 매핑 SOAP 통신**: IPortMappingService 인터페이스의 FindUpnpServicesAsync, AddPortMappingAsync를 구현하고 HTTP 기반 SOAP 프로토콜을 사용해 직접 포트를 매핑/해제합니다.

### 2. 이전 매핑 기록 추적 및 충돌 복구
- **UpnpMappingOwnershipTracker**: 이전 매핑 기록을 .tsv 형식으로 로컬 데이터 앱 폴더에 안전하게 저장합니다.
- **HandleCrashRecoveryAsync**: 서버나 런처가 비정상적으로 종료되었을 경우, 다음 런처 실행 시 남아있는 고아 UPnP 포트 포워딩 매핑 기록을 추적하여 안전하게 정리합니다.

### 3. C# 5.0 (.NET Framework 4.8) 호환성 완벽 준수
- 인라인 out 변수 제한이나 식 본문 메서드(=>), $ 문자열 보간법이 제한되는 C# 5.0 구문에 완벽하게 호환되도록 코드를 조정했습니다.
- 인코딩(UTF-8 BOM) 문제를 해결하여 Windows 환경 내 PowerShell 컴파일(csc.exe)이 오류 없이 매끄럽게 진행되도록 수정했습니다.

## 검증 결과

- uild.ps1을 사용한 전체 컴파일 및 실행 파일 빌드 **성공**
- 	est.ps1의 모든 단위 테스트(21개 테스트, TestSocketUpnpLocalServer 포함) **완벽 통과**
- 모의 환경에서 UPnP 서버(Local HttpListener)를 생성하고 AddPortMappingAsync까지 이상 없이 SOAP 통신이 오고 가는 것을 확인했습니다.

이제 이 기능을 바탕으로 로컬에서 직접 런처를 실행하여 확인해 보시거나 다음 작업을 진행하실 수 있습니다!