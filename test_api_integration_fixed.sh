#!/bin/bash

# Скрипт для тестирования взаимодействия между сервисами

echo "=== Тестирование взаимодействия между сервисами ==="
echo ""

# Базовый URL для API шлюза
GATEWAY_URL="http://localhost:8080"

# Функция для выполнения HTTP запроса и вывода результата
test_endpoint() {
    local endpoint=$1
    local method=${2:-GET}
    local data=${3:-""}
    
    echo "Тестирование: $method $endpoint"
    
    if [ "$method" = "POST" ] && [ -n "$data" ]; then
        response=$(curl -s -w "\nHTTP_CODE:%{http_code}" -X POST \
            -H "Content-Type: application/json" \
            -d "$data" \
            "$GATEWAY_URL$endpoint")
    else
        response=$(curl -s -w "\nHTTP_CODE:%{http_code}" -X "$method" "$GATEWAY_URL$endpoint")
    fi
    
    http_code=$(echo "$response" | grep -o 'HTTP_CODE:[0-9]*' | cut -d: -f2)
    body=$(echo "$response" | sed -e 's/HTTP_CODE:[0-9]*$//')
    
    echo "HTTP Status: $http_code"
    echo "Response: $body"
    echo "----------------------------------------"
}

# Тестирование health endpoints сервисов
echo "1. Проверка health endpoints сервисов:"
test_endpoint "/api/health/activity"
test_endpoint "/api/health/auth"
test_endpoint "/api/health/user"
test_endpoint "/api/health/metrics"
test_endpoint "/api/health/notification"
test_endpoint "/api/health/report"
test_endpoint "/api/health/agent"

# Тестирование аутентификации
echo ""
echo "2. Тестирование аутентификации:"
test_endpoint "/api/auth/login" "POST" '{"username":"testuser","password":"testpass"}'

# Тестирование API endpoints
echo ""
echo "3. Тестирование основных API endpoints:"
test_endpoint "/api/dashboard/stats"
test_endpoint "/api/dashboard/activities"
test_endpoint "/api/dashboard/anomalies"
test_endpoint "/api/search/activities"
test_endpoint "/api/search/anomalies"
test_endpoint "/api/search/filters"

# Тестирование пользовательских endpoints
echo ""
echo "4. Тестирование пользовательских endpoints:"
test_endpoint "/api/user/users"
test_endpoint "/api/notification/notifications"
test_endpoint "/api/metrics/metrics"

# Тестирование отчетов
echo ""
echo "5. Тестирование отчетов:"
test_endpoint "/api/reports/daily"
test_endpoint "/api/reports/weekly"
test_endpoint "/api/reports/monthly"

echo ""
echo "=== Тестирование завершено ==="