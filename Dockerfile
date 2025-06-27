# 🐳 .NET 8 패킷 캡처 서버 Dockerfile
# 멀티 스테이지 빌드로 최적화된 컨테이너 이미지 생성

# =============================================================================
# 1단계: 빌드 환경 (Build Stage)
# =============================================================================
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build-env
WORKDIR /app

# 메타데이터 레이블 추가
LABEL maintainer="your-email@domain.com"
LABEL description="Cross-platform packet capture server with .NET 8"
LABEL version="1.0.0"

# 시스템 패키지 업데이트 및 필수 도구 설치
RUN apt-get update && apt-get install -y \
    libpcap-dev \
    libpcap0.8 \
    tcpdump \
    net-tools \
    iputils-ping \
    curl \
    wget \
    && rm -rf /var/lib/apt/lists/*

# 프로젝트 파일 복사 및 의존성 복원
COPY *.csproj ./
RUN dotnet restore

# 소스 코드 복사
COPY . ./

# 애플리케이션 빌드 (Release 모드)
RUN dotnet publish -c Release -o out --no-restore

# =============================================================================
# 2단계: 런타임 환경 (Runtime Stage)
# =============================================================================
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# 런타임 패키지 설치
RUN apt-get update && apt-get install -y \
    libpcap0.8 \
    libpcap-dev \
    tcpdump \
    net-tools \
    iputils-ping \
    curl \
    && rm -rf /var/lib/apt/lists/* \
    && apt-get clean

# 논루트 사용자 생성 (보안 강화)
RUN groupadd -r appuser && useradd -r -g appuser appuser

# 빌드된 애플리케이션 복사
COPY --from=build-env /app/out .

# 웹 컨텐츠 디렉토리 생성 및 복사
RUN mkdir -p /app/wwwroot
COPY --from=build-env /app/wwwroot/ /app/wwwroot/

# 로그 디렉토리 생성
RUN mkdir -p /app/logs && chown -R appuser:appuser /app/logs

# 포트 노출
EXPOSE 8080 9001 16000

# 환경변수 설정
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_RUNNING_IN_CONTAINER=true
ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_EnableDiagnostics=0

# 헬스체크 설정
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:8080/ || exit 1

# 볼륨 마운트 포인트 설정
VOLUME ["/app/logs", "/app/config"]

# 작업 디렉토리 권한 설정
RUN chown -R appuser:appuser /app

# 컨테이너 실행 시 논루트 사용자로 전환
USER appuser

# 애플리케이션 시작 명령
ENTRYPOINT ["dotnet", "PacketCapture.dll"]
