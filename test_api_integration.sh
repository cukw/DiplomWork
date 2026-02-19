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

# Тестирование health endpoints
echo "1. Проверка health endpoints сервисов:"
test_endpoint "/health/activity"
test_endpoint "/health/auth"
test_endpoint "/health/user"
test_endpoint "/health/metrics"
test_endpoint "/health/notification"
test_endpoint "/health/report"
test_endpoint "/health/agent"

# Тестирование аутентификации
echo ""
echo "2. Тестирование аутентификации:"
test_endpoint "/auth/api/login" "POST" '{"username":"testuser","password":"testpass"}'

# Тестирование API endpoints
echo ""
echo "3. Тестирование основных API endpoints:"
test_endpoint "/dashboard/stats"
test_endpoint "/dashboard/activities"
test_endpoint "/dashboard/anomalies"
test_endpoint "/search/activities"
test_endpoint "/search/anomalies"
test_endpoint "/search/filters"

# Тестирование пользовательских endpoints
echo ""
echo "4. Тестирование пользовательских endpoints:"
test_endpoint "/user/users"
test_endpoint "/notification/notifications"
test_endpoint "/metrics/metrics"

# Тестирование отчетов
echo ""
echo "5. Тестирование отчетов:"
test_endpoint "/reports/daily"
test_endpoint "/reports/weekly"
test_endpoint "/reports/monthly"

echo ""
echo "=== Тестирование завершено ==="