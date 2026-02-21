#!/bin/bash

# Diagnostic script for the Ocelot Gateway Docker container issue

echo "=== Ocelot Gateway Docker Diagnostic Script ==="
echo

# Build the debug version of the container
echo "Building debug container..."
docker build -f dockerfile.debug -t diplomwork-gateway-debug .

echo
echo "=== Running Debug Container ==="
echo "This will show detailed information about the container environment"
echo

# Run the debug container
docker run --rm -it diplomwork-gateway-debug

echo
echo "=== If the container exits immediately, try the following commands manually ==="
echo
echo "1. Check if the container was built:"
echo "   docker images | grep diplomwork-gateway-debug"
echo
echo "2. Run a shell in the container:"
echo "   docker run --rm -it diplomwork-gateway-debug sh"
echo
echo "3. Inside the container, manually run:"
echo "   cd /app"
echo "   ls -la"
echo "   dotnet --version"
echo "   dotnet src.dll"
echo
echo "4. Check for errors in the build process:"
echo "   docker build -f dockerfile.debug -t diplomwork-gateway-debug . 2>&1 | tail -50"
echo
echo "=== Testing the Fixed Container ==="
echo "Building fixed container..."
docker build -f dockerfile.fixed -t diplomwork-gateway-fixed .

echo
echo "Running fixed container..."
docker run --rm -it diplomwork-gateway-fixed