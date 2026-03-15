FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Senf.csproj", "."]
RUN dotnet restore "Senf.csproj"

COPY . .
RUN dotnet publish "Senf.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Install dependencies
RUN apt-get update \
    && apt-get install -y --no-install-recommends openssh-client curl \
    && rm -rf /var/lib/apt/lists/*

# Copy published app
COPY --from=build /app/publish .

# Create data/config directories with correct ownership
RUN mkdir -p /app/data /app/config \
    && chown -R www-data:www-data /app/data

# SSH directory for www-data
RUN mkdir -p /home/www-data/.ssh \
    && chown -R www-data:www-data /home/www-data/.ssh

ENV HOME=/home/www-data

# Switch to non-root user
USER www-data

# Runtime entrypoint fixes volume ownership (works for fresh and existing volumes)
ENTRYPOINT ["sh", "-c", "chown -R www-data:www-data /app/data && exec dotnet Senf.dll"]
CMD ["run"]