# Senf

Senf is an environment and SSH key management service. It provides a web API for managing users, storing encrypted environment files, and managing SSH keys with SSH authentication support.

## Features

- User management with SSH public key authentication
- Storage and encryption of environment files
- Multiple SSH keys per user
- RESTful API with OpenAPI documentation
- CLI utilities for user and key management
- SQLite database with Entity Framework Core

## Requirements

- Docker and Docker Compose, or
- .NET 10.0 SDK (for local development)

## Quick Start with Docker

### 1. Build and run the container

```bash
docker-compose up -d
```

The application will be accessible at `http://localhost:5000`.

### 2. Create your first user

Open a terminal and run:

```bash
docker-compose exec senf dotnet Senf.dll add-user <username> "<public-key>" "<key-name>"
```

For example:

```bash
docker-compose exec senf dotnet Senf.dll add-user admin "ssh-rsa AAAAB3NzaC1yc2EAAA... user@host" "admin-key"
```

To get your SSH public key, run on your local machine:

```bash
cat ~/.ssh/id_rsa.pub
```

### 3. Verify the setup

List all users:

```bash
docker-compose exec senf dotnet Senf.dll list-users
```

Access the API documentation:

```
http://localhost:5000/openapi/v1.json
```

## Configuration

### Port Configuration

By default, the application listens on port 5000. To change it, edit `docker-compose.yml`:

```yaml
environment:
  - PORT=8080
ports:
  - "8080:8080"
```

Then restart:

```bash
docker-compose down
docker-compose up -d
```

Or set it via environment variable when running Docker directly:

```bash
docker run -e PORT=3000 -p 3000:3000 senf-app:latest
```

### Database Path

By default, the SQLite database is stored in a Docker volume called `senf-data`. This persists across container restarts.

To use a different location, modify `docker-compose.yml`:

```yaml
environment:
  - DATABASE_PATH=/custom/path/senf.db
volumes:
  - /host/path:/app/data
```

### Environment Variables

- `PORT`: Port to listen on (default: 5000). Must be a valid port number (1-65535).
- `DATABASE_PATH`: Path to the SQLite database file (default: /app/data/senf.db)
- `ASPNETCORE_ENVIRONMENT`: Set to "Production" in docker-compose.yml

## CLI Commands

### Add a user

```bash
docker-compose exec senf dotnet Senf.dll add-user <username> "<public-key>" "<key-name>"
```

Arguments:
- `username`: The username for the new user
- `public-key`: The SSH public key (quoted)
- `key-name`: Friendly name for the key

### Add an SSH key to existing user

```bash
docker-compose exec senf dotnet Senf.dll add-key <user-id> "<public-key>" "<key-name>"
```

Arguments:
- `user-id`: The numeric ID of the user (get from list-users)
- `public-key`: The SSH public key (quoted)
- `key-name`: Friendly name for the key

### List all users and keys

```bash
docker-compose exec senf dotnet Senf.dll list-users
```

## Development

For local development without Docker:

1. Install .NET 10.0 SDK
2. Install SQLite (or rely on the NuGet package)
3. Run the application:

```bash
dotnet run
```

Or with CLI commands:

```bash
dotnet run -- add-user testuser "ssh-rsa AAAA..." "test-key"
dotnet run -- list-users
```

The database will be created in your user's ApplicationData folder.

## Stopping and Cleaning Up

Stop the containers:

```bash
docker-compose down
```

Stop and remove all data:

```bash
docker-compose down -v
```

This removes the database volume as well.

## Logs

View application logs:

```bash
docker-compose logs -f senf
```

## Health Check

The container includes a health check that verifies the API is responding. Check the status:

```bash
docker ps
```

Look for the "healthy" or "unhealthy" status in the output.
