#!/bin/bash

# 🐳 Docker 빌드 및 실행 스크립트
# 패킷 캡처 서버를 위한 자동화된 배포 스크립트

set -e  # 오류 시 즉시 종료

# 색상 코드 정의
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# 로그 함수들
log_info() {
    echo -e "${BLUE}[INFO]${NC} $1"
}

log_success() {
    echo -e "${GREEN}[SUCCESS]${NC} $1"
}

log_warning() {
    echo -e "${YELLOW}[WARNING]${NC} $1"
}

log_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# 도커 설치 확인
check_docker() {
    log_info "Docker 설치 상태 확인 중..."
    
    if ! command -v docker &> /dev/null; then
        log_error "Docker가 설치되지 않았습니다."
        log_info "Docker 설치: https://docs.docker.com/get-docker/"
        exit 1
    fi
    
    if ! docker info &> /dev/null; then
        log_error "Docker 데몬이 실행되지 않았습니다."
        log_info "Docker를 시작하세요: sudo systemctl start docker"
        exit 1
    fi
    
    log_success "Docker가 정상적으로 실행 중입니다."
}

# 이미지 빌드
build_image() {
    log_info "패킷 캡처 서버 이미지 빌드 중..."
    
    # 빌드 컨텍스트 확인
    if [ ! -f "Dockerfile" ]; then
        log_error "Dockerfile을 찾을 수 없습니다."
        exit 1
    fi
    
    if [ ! -f "PacketCapture.csproj" ]; then
        log_error "PacketCapture.csproj를 찾을 수 없습니다."
        exit 1
    fi
    
    # 이미지 빌드
    docker build -t packet-capture-server:latest . || {
        log_error "이미지 빌드에 실패했습니다."
        exit 1
    }
    
    log_success "이미지 빌드가 완료되었습니다."
}

# 필요한 디렉토리 생성
setup_directories() {
    log_info "필요한 디렉토리 생성 중..."
    
    mkdir -p logs
    mkdir -p config
    
    # 권한 설정 (필요한 경우)
    chmod 755 logs
    chmod 755 config
    
    log_success "디렉토리 설정이 완료되었습니다."
}

# 컨테이너 실행
run_container() {
    log_info "패킷 캡처 서버 컨테이너 시작 중..."
    
    # 기존 컨테이너 정리
    if docker ps -a --format 'table {{.Names}}' | grep -q packet-capture-server; then
        log_warning "기존 컨테이너를 중지하고 제거합니다..."
        docker stop packet-capture-server 2>/dev/null || true
        docker rm packet-capture-server 2>/dev/null || true
    fi
    
    # 컨테이너 실행
    docker run -d \
        --name packet-capture-server \
        --network host \
        --privileged \
        --cap-add NET_ADMIN \
        --cap-add NET_RAW \
        --cap-add SYS_ADMIN \
        -e ASPNETCORE_ENVIRONMENT=Production \
        -e TZ=Asia/Seoul \
        -v "$(pwd)/logs:/app/logs:rw" \
        -v "$(pwd)/config:/app/config:ro" \
        -v "$(pwd)/wwwroot:/app/wwwroot:ro" \
        --restart unless-stopped \
        packet-capture-server:latest || {
        log_error "컨테이너 실행에 실패했습니다."
        exit 1
    }
    
    log_success "컨테이너가 성공적으로 시작되었습니다."
}

# Docker Compose 사용
run_with_compose() {
    log_info "Docker Compose로 서비스 시작 중..."
    
    if [ ! -f "docker-compose.yml" ]; then
        log_error "docker-compose.yml을 찾을 수 없습니다."
        exit 1
    fi
    
    # 서비스 중지 및 제거
    docker-compose down 2>/dev/null || true
    
    # 서비스 시작
    docker-compose up -d || {
        log_error "Docker Compose 실행에 실패했습니다."
        exit 1
    }
    
    log_success "Docker Compose 서비스가 시작되었습니다."
}

# 컨테이너 상태 확인
check_status() {
    log_info "컨테이너 상태 확인 중..."
    
    # 컨테이너 실행 상태
    if docker ps --format 'table {{.Names}}\t{{.Status}}\t{{.Ports}}' | grep packet-capture-server; then
        log_success "컨테이너가 정상적으로 실행 중입니다."
        
        # 헬스체크 대기
        log_info "헬스체크 대기 중... (최대 60초)"
        for i in {1..12}; do
            if curl -f http://localhost:8080/ &>/dev/null; then
                log_success "웹 서버가 정상적으로 응답합니다."
                break
            fi
            
            if [ $i -eq 12 ]; then
                log_warning "웹 서버 응답을 확인할 수 없습니다."
            else
                echo -n "."
                sleep 5
            fi
        done
    else
        log_error "컨테이너가 실행되지 않았습니다."
        
        # 로그 출력
        log_info "컨테이너 로그:"
        docker logs packet-capture-server 2>/dev/null || true
        exit 1
    fi
}

# 로그 출력
show_logs() {
    log_info "실시간 로그 출력 (Ctrl+C로 종료):"
    docker logs -f packet-capture-server
}

# 컨테이너 중지
stop_container() {
    log_info "컨테이너 중지 중..."
    
    if docker ps --format 'table {{.Names}}' | grep -q packet-capture-server; then
        docker stop packet-capture-server
        log_success "컨테이너가 중지되었습니다."
    else
        log_warning "실행 중인 컨테이너가 없습니다."
    fi
}

# 사용법 출력
usage() {
    echo "사용법: $0 [명령]"
    echo ""
    echo "명령:"
    echo "  build     - 이미지 빌드"
    echo "  run       - 컨테이너 실행"
    echo "  compose   - Docker Compose로 실행"
    echo "  status    - 상태 확인"
    echo "  logs      - 로그 출력"
    echo "  stop      - 컨테이너 중지"
    echo "  restart   - 재시작 (빌드 + 실행)"
    echo "  clean     - 이미지 및 컨테이너 정리"
    echo ""
    echo "예시:"
    echo "  $0 restart    # 전체 재배포"
    echo "  $0 logs       # 실시간 로그 확인"
}

# 정리 작업
clean_all() {
    log_info "이미지 및 컨테이너 정리 중..."
    
    # 컨테이너 중지 및 제거
    docker stop packet-capture-server 2>/dev/null || true
    docker rm packet-capture-server 2>/dev/null || true
    
    # 이미지 제거
    docker rmi packet-capture-server:latest 2>/dev/null || true
    
    # Docker Compose 정리
    docker-compose down --rmi all 2>/dev/null || true
    
    log_success "정리가 완료되었습니다."
}

# 메인 로직
main() {
    case "${1:-}" in
        "build")
            check_docker
            build_image
            ;;
        "run")
            check_docker
            setup_directories
            run_container
            check_status
            ;;
        "compose")
            check_docker
            setup_directories
            run_with_compose
            check_status
            ;;
        "status")
            check_status
            ;;
        "logs")
            show_logs
            ;;
        "stop")
            stop_container
            ;;
        "restart")
            check_docker
            stop_container
            build_image
            setup_directories
            run_container
            check_status
            ;;
        "clean")
            clean_all
            ;;
        *)
            usage
            exit 1
            ;;
    esac
}

# 스크립트 실행
main "$@"
