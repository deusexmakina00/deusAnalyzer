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

### 명령줄 옵션

프로그램 실행 시 다양한 옵션을 사용할 수 있습니다:

```bash
# 기본 실행 (패킷 저장 없음)
PacketCapture.exe

# 패킷 재생 모드 (기본 위치에서 재생)
PacketCapture.exe --replay

# 패킷 재생 모드 (사용자 지정 위치에서 재생)
PacketCapture.exe --replay --save-directory "D:\MyPackets"

# 패킷 저장 활성화 (기본 위치: C:\Packets)
PacketCapture.exe --save-packets

# 패킷 저장 활성화 및 사용자 지정 위치
PacketCapture.exe --save-packets --save-directory "D:\MyPackets"

# 단축 옵션 사용
PacketCapture.exe -s -d "D:\MyPackets"

# 재생 모드 단축 옵션
PacketCapture.exe -r -d "D:\MyPackets"

# 도움말 표시
PacketCapture.exe --help
```

**사용 가능한 옵션:**
- `--replay`, `-r`: 저장된 패킷 재생 모드 (대화형 메뉴 제공)
- `--save-packets`, `-s`: 패킷을 디스크에 저장 활성화
- `--save-directory <경로>`, `-d <경로>`: 패킷 저장/재생 디렉토리 지정 (기본값: `C:\Packets`)
- `--help`, `-h`: 도움말 메시지 표시

**참고**: `--save-directory` 옵션은 패킷 저장 시에는 저장 위치를, 재생 시에는 재생할 패킷을 찾을 위치를 지정합니다.

### 패킷 재생 모드

`--replay` 옵션을 사용하면 이전에 저장된 패킷들을 재생할 수 있습니다:

1. **세션 선택**: 저장된 날짜별 패킷 세션 목록에서 선택
2. **시간 범위 선택**: 특정 시간대의 패킷만 재생 가능
3. **재생 속도 조절**: 1x, 2x, 5x, 10x 속도로 재생
4. **실시간 분석**: 재생 중에도 웹 인터페이스에서 스킬 분석 확인

#### 재생 위치 설정

```bash
# 기본 위치(C:\Packets)에서 재생
PacketCapture.exe --replay

# 사용자 지정 위치에서 재생
PacketCapture.exe --replay --save-directory "D:\MyPackets"

# 단축 옵션으로 재생
PacketCapture.exe -r -d "D:\MyPackets"
```

#### 재생 메뉴 예시

재생 모드 실행 시 대화형 명령어 인터페이스가 제공됩니다:

```bash
# 재생 모드 실행
PacketCapture.exe --replay

# 사용 가능한 명령어들:
replay> today                    # 오늘 세션 재생
replay> yesterday               # 어제 세션 재생  
replay> date 2025-06-28         # 특정 날짜 재생
replay> range 2025-06-25 2025-06-28  # 날짜 범위 재생
replay> list 2025-06-28         # 특정 날짜의 세션 목록 표시
replay> stop                    # 현재 재생 중지
replay> quit                    # 재생 모드 종료
```

#### 재생 모드 명령어

재생 모드에서 사용할 수 있는 텍스트 명령어들:

```
세션 재생:
  today                 - 오늘의 모든 세션 재생
  yesterday            - 어제의 모든 세션 재생
  date YYYY-MM-DD      - 특정 날짜의 세션 재생
  range YYYY-MM-DD YYYY-MM-DD  - 날짜 범위의 세션 재생

정보 조회:
  list YYYY-MM-DD      - 특정 날짜의 세션 목록 표시

제어:
  stop                 - 현재 재생 중지
  quit, exit           - 재생 모드 종료

사용 예시:
  replay> date 2025-06-28
  replay> range 2025-06-25 2025-06-28
  replay> list 2025-06-28
```

#### 패킷 폴더 구조

재생할 패킷들은 다음과 같은 구조로 저장되어 있어야 합니다:

```
지정된_디렉토리/
├── 2025-06-28/                    # 날짜별 폴더
│   ├── 14-30-15/                  # 시간별 폴더
│   │   ├── seq_00000001/          # 시퀀스별 폴더
│   │   │   ├── packet_000_type_*.bin
│   │   │   ├── packet_000_type_*.meta
│   │   │   └── packets_summary.txt
│   │   └── seq_00000002/
│   └── 15-20-30/
└── 2025-06-27/
```

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
├── PacketConfigManager.cs         # Lua 기반 패킷 필터링 관리
├── NpcapPacketCapture.cs          # 크로스 플랫폼 패킷 캡처
├── ModernWebSocketServer.cs       # WebSocket 서버 구현
├── SkillMatcher.cs                # 🎯 스킬 매칭 시스템 (Lua 기반)
├── StaticWebServer.cs             # HTTP 정적 파일 서버
├── PacketModels.cs                # 데이터 모델 및 파싱
├── PacketExtractor.cs             # 패킷 추출 유틸리티 (Lua 필터링 지원)
├── PacketPlayer.cs                # 패킷 재생 엔진
├── PacketReplayManager.cs         # 패킷 재생 관리
├── BinaryObfuscator.cs            # 바이너리 난독화 유틸리티
├── NLog.config                    # 로깅 구성
├── PacketCapture.csproj           # 프로젝트 파일
├── scripts/
│   ├── packet_config.lua          # Lua 패킷 필터링 설정
│   ├── skill_matcher_framework.lua # 🏗️ SkillMatcher 프레임워크 (수정 금지)
│   ├── skill_matcher.lua          # ✨ 사용자 커스터마이징 영역
│   ├── skill_matcher_template.lua # 📋 빠른 시작 템플릿
│   └── skill_matcher_user.lua     # 📚 고급 예시 모음
└── wwwroot/                       # 웹 인터페이스 파일
    ├── index.html
    ├── style.css
    ├── app.js
    └── translation.js
```

## 🎯 SkillMatcher 시스템

**새로워진 사용자 친화적 SkillMatcher!** 이제 복잡한 코드 없이 간단한 커스터마이징만으로 원하는 매칭 동작을 구현할 수 있습니다.

### 빠른 시작

1. **기본 사용**: 아무것도 수정하지 않아도 완벽하게 작동합니다
2. **간단한 커스터마이징**: `skill_matcher.lua`에서 원하는 예시의 주석만 해제
3. **고급 사용**: `skill_matcher_user.lua`의 예시를 참고하여 복잡한 로직 작성

### 파일 설명

- **📋 skill_matcher_template.lua**: 빠른 시작을 위한 템플릿
- **✨ skill_matcher.lua**: 사용자가 커스터마이징하는 파일  
- **📚 skill_matcher_user.lua**: 고급 예시와 설명
- **🏗️ skill_matcher_framework.lua**: 모든 기본 기능 (건드리지 마세요!)

### 커스터마이징 예시

```lua
-- skill_matcher.lua에서 주석 해제 후 수정
function customCanBeChannelingSkill(skill, damageTime)
    -- 라이트닝 스킬은 항상 즉시 스킬로 처리
    if string.find(skill.SkillName, "Lightning") then
        return false
    end
    return canBeChannelingSkill(skill, damageTime) -- 기본 로직
end
```

**📖 자세한 가이드**: `SKILLMATCHER_GUIDE.md` 파일을 참고하세요!

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

#### Lua 필터링 오류
```
오류: Lua 설정 파일을 로드할 수 없음
```
**해결 방법:**
1. `scripts/packet_config.lua` 파일 존재 확인
2. Lua 스크립트 문법 오류 확인
3. 기본 설정으로 실행: 설정 파일이 없으면 자동으로 기본값 사용

```
오류: 패킷 필터링이 작동하지 않음
```
**해결 방법:**
1. Lua 함수 이름 확인: `shouldExcludePacket` 또는 `shouldExcludePacketAdvanced`
2. 로그에서 Lua 오류 메시지 확인
3. 설정 파일 재로드: 파일 수정 후 자동 재로드됨

#### SkillMatcher 오류
```
오류: [LuaEngine] Error in CleanupOldSkills 또는 매칭 실패
```
**해결 방법:**
1. **기본 동작부터 확인**: `skill_matcher.lua`의 모든 커스텀 함수를 주석 처리
2. **프레임워크 파일 확인**: `skill_matcher_framework.lua` 파일이 존재하는지 확인
3. **템플릿 사용**: `skill_matcher_template.lua`를 `skill_matcher.lua`로 복사
4. **로그 확인**: `[LuaEngine]` 태그로 시작하는 오류 메시지 확인

```
오류: 스킬이 매칭되지 않음
```
**해결 방법:**
1. 커스텀 필터 함수(`customSkillFilter`)가 너무 제한적이지 않은지 확인
2. 로그에서 `No skill match found` 메시지 확인
3. 기본 동작으로 되돌린 후 단계별로 커스터마이징 추가

#### 빌드 오류
```
오류: 종속성 패키지를 찾을 수 없음
```
**해결 방법:**
1. NuGet 패키지 복원: `dotnet restore`
2. .NET SDK 업데이트: Microsoft에서 최신 버전 다운로드
3. 정리 후 재빌드: `dotnet clean && dotnet build`