@echo off
REM 🐳 Windows용 Docker 빌드 및 실행 스크립트
REM 패킷 캡처 서버를 위한 자동화된 배포 스크립트

setlocal enabledelayedexpansion

REM 색상 코드 (Windows에서는 제한적)
set "INFO=[INFO]"
set "SUCCESS=[SUCCESS]"
set "WARNING=[WARNING]"
set "ERROR=[ERROR]"

:check_docker
echo %INFO% Docker 설치 상태 확인 중...

docker --version >nul 2>&1
if errorlevel 1 (
    echo %ERROR% Docker가 설치되지 않았습니다.
    echo Docker 설치: https://docs.docker.com/desktop/windows/
    pause
    exit /b 1
)

docker info >nul 2>&1
if errorlevel 1 (
    echo %ERROR% Docker 데몬이 실행되지 않았습니다.
    echo Docker Desktop을 시작하세요.
    pause
    exit /b 1
)

echo %SUCCESS% Docker가 정상적으로 실행 중입니다.
goto :eof

:build_image
echo %INFO% 패킷 캡처 서버 이미지 빌드 중...

if not exist "Dockerfile" (
    echo %ERROR% Dockerfile을 찾을 수 없습니다.
    pause
    exit /b 1
)

if not exist "PacketCapture.csproj" (
    echo %ERROR% PacketCapture.csproj를 찾을 수 없습니다.
    pause
    exit /b 1
)

docker build -t packet-capture-server:latest .
if errorlevel 1 (
    echo %ERROR% 이미지 빌드에 실패했습니다.
    pause
    exit /b 1
)

echo %SUCCESS% 이미지 빌드가 완료되었습니다.
goto :eof

:setup_directories
echo %INFO% 필요한 디렉토리 생성 중...

if not exist "logs" mkdir logs
if not exist "config" mkdir config

echo %SUCCESS% 디렉토리 설정이 완료되었습니다.
goto :eof

:run_container
echo %INFO% 패킷 캡처 서버 컨테이너 시작 중...

REM 기존 컨테이너 정리
docker ps -a --format "table {{.Names}}" | findstr "packet-capture-server" >nul 2>&1
if not errorlevel 1 (
    echo %WARNING% 기존 컨테이너를 중지하고 제거합니다...
    docker stop packet-capture-server >nul 2>&1
    docker rm packet-capture-server >nul 2>&1
)

REM 컨테이너 실행 (Windows에서는 host 네트워크 모드 제한적)
docker run -d ^
    --name packet-capture-server ^
    -p 8080:8080 ^
    -p 8081:8081 ^
    -p 16000:16000 ^
    -e ASPNETCORE_ENVIRONMENT=Production ^
    -v "%cd%\logs:/app/logs" ^
    -v "%cd%\config:/app/config" ^
    -v "%cd%\wwwroot:/app/wwwroot" ^
    --restart unless-stopped ^
    packet-capture-server:latest

if errorlevel 1 (
    echo %ERROR% 컨테이너 실행에 실패했습니다.
    pause
    exit /b 1
)

echo %SUCCESS% 컨테이너가 성공적으로 시작되었습니다.
goto :eof

:run_with_compose
echo %INFO% Docker Compose로 서비스 시작 중...

if not exist "docker-compose.yml" (
    echo %ERROR% docker-compose.yml을 찾을 수 없습니다.
    pause
    exit /b 1
)

REM 서비스 중지 및 제거
docker-compose down >nul 2>&1

REM 서비스 시작
docker-compose up -d
if errorlevel 1 (
    echo %ERROR% Docker Compose 실행에 실패했습니다.
    pause
    exit /b 1
)

echo %SUCCESS% Docker Compose 서비스가 시작되었습니다.
goto :eof

:check_status
echo %INFO% 컨테이너 상태 확인 중...

docker ps --format "table {{.Names}}	{{.Status}}	{{.Ports}}" | findstr "packet-capture-server"
if errorlevel 1 (
    echo %ERROR% 컨테이너가 실행되지 않았습니다.
    echo %INFO% 컨테이너 로그:
    docker logs packet-capture-server 2>nul
    pause
    exit /b 1
)

echo %SUCCESS% 컨테이너가 정상적으로 실행 중입니다.

REM 헬스체크 (간단한 ping)
echo %INFO% 웹 서버 응답 확인 중...
timeout /t 10 /nobreak >nul
curl -f http://localhost:8080/ >nul 2>&1
if errorlevel 1 (
    echo %WARNING% 웹 서버 응답을 확인할 수 없습니다.
) else (
    echo %SUCCESS% 웹 서버가 정상적으로 응답합니다.
)
goto :eof

:show_logs
echo %INFO% 실시간 로그 출력 (Ctrl+C로 종료):
docker logs -f packet-capture-server
goto :eof

:stop_container
echo %INFO% 컨테이너 중지 중...

docker ps --format "table {{.Names}}" | findstr "packet-capture-server" >nul 2>&1
if not errorlevel 1 (
    docker stop packet-capture-server
    echo %SUCCESS% 컨테이너가 중지되었습니다.
) else (
    echo %WARNING% 실행 중인 컨테이너가 없습니다.
)
goto :eof

:clean_all
echo %INFO% 이미지 및 컨테이너 정리 중...

REM 컨테이너 중지 및 제거
docker stop packet-capture-server >nul 2>&1
docker rm packet-capture-server >nul 2>&1

REM 이미지 제거
docker rmi packet-capture-server:latest >nul 2>&1

REM Docker Compose 정리
docker-compose down --rmi all >nul 2>&1

echo %SUCCESS% 정리가 완료되었습니다.
goto :eof

:usage
echo 사용법: %0 [명령]
echo.
echo 명령:
echo   build     - 이미지 빌드
echo   run       - 컨테이너 실행
echo   compose   - Docker Compose로 실행
echo   status    - 상태 확인
echo   logs      - 로그 출력
echo   stop      - 컨테이너 중지
echo   restart   - 재시작 (빌드 + 실행)
echo   clean     - 이미지 및 컨테이너 정리
echo.
echo 예시:
echo   %0 restart    # 전체 재배포
echo   %0 logs       # 실시간 로그 확인
pause
goto :eof

REM 메인 로직
if "%1"=="build" (
    call :check_docker
    call :build_image
) else if "%1"=="run" (
    call :check_docker
    call :setup_directories
    call :run_container
    call :check_status
) else if "%1"=="compose" (
    call :check_docker
    call :setup_directories
    call :run_with_compose
    call :check_status
) else if "%1"=="status" (
    call :check_status
) else if "%1"=="logs" (
    call :show_logs
) else if "%1"=="stop" (
    call :stop_container
) else if "%1"=="restart" (
    call :check_docker
    call :stop_container
    call :build_image
    call :setup_directories
    call :run_container
    call :check_status
) else if "%1"=="clean" (
    call :clean_all
) else (
    call :usage
)

pause
