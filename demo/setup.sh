#!/bin/bash
#
# FluxIndex Demo Setup Script for Linux/Mac
# Usage: ./setup.sh [start|stop|restart|status|clean|logs|build|test] [service]
#

set -e

DEMO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
COMPOSE_FILE="$DEMO_DIR/docker-compose.yml"
ENV_FILE="$DEMO_DIR/.env"
ENV_EXAMPLE_FILE="$DEMO_DIR/.env.example"

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

print_header() {
    echo ""
    echo -e "${CYAN}========================================${NC}"
    echo -e "${CYAN} $1${NC}"
    echo -e "${CYAN}========================================${NC}"
    echo ""
}

print_success() {
    echo -e "${GREEN}[OK]${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}[WARN]${NC} $1"
}

print_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

check_docker() {
    if ! docker info > /dev/null 2>&1; then
        print_error "Docker is not running. Please start Docker."
        exit 1
    fi
}

init_env() {
    if [ ! -f "$ENV_FILE" ]; then
        if [ -f "$ENV_EXAMPLE_FILE" ]; then
            cp "$ENV_EXAMPLE_FILE" "$ENV_FILE"
            print_success "Created .env from .env.example"
        else
            print_warning ".env.example not found"
        fi
    fi
}

start_services() {
    print_header "Starting FluxIndex Demo Services"
    check_docker
    init_env

    cd "$DEMO_DIR"

    if [ "$1" = "all" ] || [ -z "$1" ]; then
        docker compose up -d
    else
        docker compose up -d "$1"
    fi

    echo ""
    print_success "Services started successfully!"
    echo ""
    echo -e "${YELLOW}Service URLs:${NC}"
    echo "  PostgreSQL: localhost:5432"
    echo "  Neo4j:      http://localhost:7474 (Browser)"
    echo "  Neo4j Bolt: bolt://localhost:7687"
    echo "  Redis:      localhost:6379"
    echo ""
    echo -e "${YELLOW}To start the demo application:${NC}"
    echo "  cd FluxIndex.Demo && dotnet run"
    echo ""
}

stop_services() {
    print_header "Stopping FluxIndex Demo Services"
    cd "$DEMO_DIR"

    if [ "$1" = "all" ] || [ -z "$1" ]; then
        docker compose down
    else
        docker compose stop "$1"
    fi

    print_success "Services stopped"
}

restart_services() {
    stop_services "$1"
    start_services "$1"
}

show_status() {
    print_header "FluxIndex Demo Service Status"
    cd "$DEMO_DIR"

    docker compose ps
    echo ""
    echo -e "${YELLOW}Health Checks:${NC}"

    # PostgreSQL
    if docker compose exec -T postgres pg_isready -U fluxindex > /dev/null 2>&1; then
        print_success "PostgreSQL: Healthy"
    else
        print_warning "PostgreSQL: Not Ready"
    fi

    # Neo4j
    if curl -s http://localhost:7474 > /dev/null 2>&1; then
        print_success "Neo4j: Healthy"
    else
        print_warning "Neo4j: Not Ready"
    fi

    # Redis
    if docker compose exec -T redis redis-cli ping 2>&1 | grep -q "PONG"; then
        print_success "Redis: Healthy"
    else
        print_warning "Redis: Not Ready"
    fi
}

clean_services() {
    print_header "Cleaning FluxIndex Demo Environment"
    echo -n "This will remove all containers and volumes. Continue? (y/N) "
    read -r confirm

    if [ "$confirm" != "y" ] && [ "$confirm" != "Y" ]; then
        echo "Cancelled."
        return
    fi

    cd "$DEMO_DIR"
    docker compose down -v --remove-orphans
    print_success "All containers and volumes removed"
}

show_logs() {
    print_header "FluxIndex Demo Service Logs"
    cd "$DEMO_DIR"

    if [ "$1" = "all" ] || [ -z "$1" ]; then
        docker compose logs --tail=100 -f
    else
        docker compose logs --tail=100 -f "$1"
    fi
}

build_demo() {
    print_header "Building FluxIndex Demo Application"
    cd "$DEMO_DIR/FluxIndex.Demo"

    if dotnet build; then
        print_success "Build completed successfully"
    else
        print_error "Build failed"
        exit 1
    fi
}

test_api() {
    print_header "Testing FluxIndex Demo API"

    BASE_URL="http://localhost:5000"

    echo -e "${YELLOW}Testing endpoints...${NC}"

    # Health check
    if health=$(curl -s "$BASE_URL/api/health" 2>/dev/null); then
        status=$(echo "$health" | grep -o '"Status":"[^"]*"' | cut -d'"' -f4)
        print_success "Health: $status"
    else
        print_error "Health check failed. Make sure the demo application is running (dotnet run)"
        return
    fi

    # Status
    if status_resp=$(curl -s "$BASE_URL/api/status" 2>/dev/null); then
        docs=$(echo "$status_resp" | grep -o '"TotalDocuments":[0-9]*' | cut -d':' -f2)
        chunks=$(echo "$status_resp" | grep -o '"TotalChunks":[0-9]*' | cut -d':' -f2)
        print_success "Status: $docs docs, $chunks chunks"
    else
        print_warning "Status endpoint failed"
    fi

    # Documents
    if docs_resp=$(curl -s "$BASE_URL/api/documents" 2>/dev/null); then
        print_success "Documents: Retrieved successfully"
    else
        print_warning "Documents endpoint failed"
    fi

    echo ""
    echo -e "${GREEN}API tests completed!${NC}"
}

# Main
ACTION="${1:-start}"
SERVICE="${2:-all}"

case "$ACTION" in
    start)   start_services "$SERVICE" ;;
    stop)    stop_services "$SERVICE" ;;
    restart) restart_services "$SERVICE" ;;
    status)  show_status ;;
    clean)   clean_services ;;
    logs)    show_logs "$SERVICE" ;;
    build)   build_demo ;;
    test)    test_api ;;
    *)
        echo "Usage: $0 [start|stop|restart|status|clean|logs|build|test] [service]"
        echo "  service: all, postgres, neo4j, redis"
        exit 1
        ;;
esac
