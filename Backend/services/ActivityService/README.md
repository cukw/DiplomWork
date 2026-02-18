# ActivityService

A gRPC microservice for tracking and analyzing user activities with anomaly detection capabilities.

## Features

- **Activity Tracking**: Store and retrieve user activities with various metadata
- **Anomaly Detection**: Automatically detect suspicious activities based on configurable rules
- **Statistics**: Generate activity statistics and reports
- **Event Publishing**: Publish activity creation events via RabbitMQ using MassTransit

## API Endpoints

### gRPC Services

1. **GetActivities** - Retrieve activities with filtering options
   - Filter by computer ID, activity type, date range
   - Support for pagination
   - Option to get only blocked activities

2. **CreateActivity** - Create a new activity
   - Automatic anomaly detection
   - Event publishing to RabbitMQ

3. **GetActivityById** - Retrieve a specific activity by ID

4. **UpdateActivity** - Update an existing activity
   - Partial updates supported
   - Re-runs anomaly detection

5. **DeleteActivity** - Delete an activity

6. **GetAnomalies** - Retrieve anomalies for activities

7. **GetActivityStatistics** - Get statistics about activities
   - Total count, blocked count, anomaly count
   - Activity type distribution
   - Average risk score

## Anomaly Detection Rules

The service automatically detects anomalies based on the following rules:

1. **High Risk Score** - Activities with risk score >= 80
2. **Suspicious Activity Types** - MALWARE, DATA_EXFILTRATION, UNAUTHORIZED_ACCESS
3. **Unusual Duration** - Activities longer than 24 hours
4. **Blocked Activities** - Any activity marked as blocked
5. **Repeated Activities** - More than 10 similar activities from the same computer in 1 hour

## Data Model

### Activity
- Id (long)
- ComputerId (int)
- Timestamp (DateTime)
- ActivityType (string)
- Details (JSONB)
- DurationMs (int?)
- Url (string?)
- ProcessName (string?)
- IsBlocked (bool)
- RiskScore (decimal?)
- Synced (bool)

### Anomaly
- Id (int)
- ActivityId (long)
- Type (string)
- Description (string?)
- DetectedAt (DateTime)

## Configuration

The service uses the following configuration:

- **Database**: PostgreSQL via Entity Framework Core
- **Message Broker**: RabbitMQ via MassTransit
- **gRPC Reflection**: Enabled in development mode

## Running the Service

### Using Docker Compose

```bash
docker-compose up activityservice
```

### Locally

```bash
cd Backend/services/ActivityService
dotnet run
```

## Testing

Run the unit tests:

```bash
cd Backend/services/ActivityService.Tests
dotnet test
```

## Environment Variables

- `ConnectionStrings__DefaultConnection`: PostgreSQL connection string
- RabbitMQ configuration is hardcoded for the docker environment

## Dependencies

- .NET 8.0
- Entity Framework Core with PostgreSQL
- gRPC
- MassTransit with RabbitMQ
- xUnit for testing