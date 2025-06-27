## 🔧 필수 요구사항

### .NET 8 SDK 설치

Microsoft 공식 웹사이트에서 .NET 8 SDK를 다운로드하고 설치

**📥 다운로드 링크:**
- **종합**: [dotnet-sdk]https://dotnet.microsoft.com/ko-kr/download

**설치 확인:**
```bash
dotnet --version
# 출력 결과: 8.0.411 이상
```

### 플랫폼별 요구사항

#### Windows
- **Npcap**: [https://npcap.org/](https://npcap.org/)에서 다운로드
- **관리자 권한**: 패킷 캡처를 위해 필요

#### Linux
```bash
# Ubuntu/Debian
sudo apt update
sudo apt install libpcap-dev

# CentOS/RHEL/Fedora
sudo yum install libpcap-devel
# 또는
sudo dnf install libpcap-devel
```

#### macOS
```bash
# libpcap은 Xcode 명령줄 도구에 포함되어 있습니다
xcode-select --install
```

### 실행
- 환경에 따라 start.bat를 관리자 권한으로 실행(Windows)
- 리눅스나 맥은 start.sh 실행

### URL 예약 (Windows)
관리자 권한 없이 모든 네트워크 인터페이스에 바인딩하려면 아래의 작업이 한번 필요함.
```cmd
# 관리자 권한으로 실행
netsh http add urlacl url=http://*:8080/ user=Everyone
netsh http add urlacl url=http://*:9001/ user=Everyone
```

## 🌐 웹 인터페이스
실행 하고 나서 사용은 아래의 주소 중 하나를 사용해서 접속하면 됨.
이 애플리케이션은 다음 주소에서 웹 인터페이스를 제공합니다:
- **로컬**: http://localhost:8080/
- **네트워크**: http://[your-ip]:8080/


## 🛠️ 개발

### 프로젝트 구조

```
capture-csharp/
├── Program.cs                      # 애플리케이션 진입점
├── PacketCaptureManager.cs        # 메인 패킷 캡처 로직
├── NpcapPacketCapture.cs          # 크로스 플랫폼 패킷 캡처
├── ModernWebSocketServer.cs       # WebSocket 서버 구현
├── SkillMatcher.cs                # 스킬 명칭 매칭 규칙
├── StaticWebServer.cs             # HTTP 정적 파일 서버
├── PacketModels.cs                # 데이터 모델 및 파싱
├── PacketExtractor.cs             # 패킷 추출 유틸리티
├── BinaryObfuscator.cs            # 바이너리 난독화 유틸리티
├── NLog.config                    # 로깅 구성
├── PacketCapture.csproj           # 프로젝트 파일
└── wwwroot/                       # 웹 인터페이스 파일
    ├── index.html
    ├── style.css
    ├── app.js
    └── translation.js
```
## 🚨 문제 해결

### 일반적인 문제

#### 패킷 캡처 실패
```
오류: 패킷 캡처 초기화 실패
```
**해결 방법:**
1. 관리자 권한(Windows) 또는 sudo(Linux/macOS)로 실행
2. Npcap(Windows) 또는 libpcap-dev(Linux) 설치
3. 방화벽 설정 확인

#### 포트 바인딩 실패
```
오류: 포트 8080에 바인딩할 수 없음
```
**해결 방법:**
1. 포트 사용 여부 확인: `netstat -an | grep :8080`
2. 관리자 권한으로 실행
3. URL 예약 사용: `netsh http add urlacl url=http://*:8080/ user=Everyone`

**해결 방법:**
1. NuGet 패키지 복원: `dotnet restore`
2. .NET SDK 업데이트: Microsoft에서 최신 버전 다운로드
3. 정리 후 재빌드: `dotnet clean && dotnet build`