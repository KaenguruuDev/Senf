FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*
RUN touch /app/data/senf.db || true
RUN chmod 664 /app/data/senf.db


COPY ["Senf.csproj", "."]

RUN dotnet restore "Senf.csproj"

COPY . .

RUN dotnet build "Senf.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Senf.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=publish /app/publish .

RUN mkdir -p /app/data

EXPOSE 5000

ENV PORT=5000
ENV DATABASE_PATH=/app/data/senf.db

HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:5000/openapi/v1.json || exit 1

USER www-data
ENTRYPOINT ["dotnet", "Senf.dll", "run"]
