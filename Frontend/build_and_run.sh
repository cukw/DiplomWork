#!/bin/bash

# Скрипт для сборки и запуска фронтенда в Docker

echo "Сборка и запуск фронтенда..."

# Проверяем, запущен ли docker-compose
if ! docker-compose ps | grep -q "Up"; then
    echo "Запуск всех сервисов..."
    docker-compose up -d
else
    echo "Пересборка и запуск фронтенда..."
    docker-compose up -d --build frontend
fi

echo "Фронтенд доступен по адресу: http://localhost:3000"
echo "Для просмотра логов выполните: docker-compose logs -f frontend"