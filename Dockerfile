FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Senf.csproj", "."]
RUN dotnet restore "Senf.csproj"

COPY . .
RUN dotnet publish "Senf.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends openssh-client curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

RUN mkdir -p /app/data \
    && chown -R www-data:www-data /app 

RUN mkdir -p /home/www-data/.ssh
RUN chown -R www-data:www-data /home/www-data/.ssh

ENV export HOME=/home/www-data
ENV DATABASE_PATH=/app/data/senf.db

USER www-data
ENTRYPOINT ["dotnet", "Senf.dll"]
CMD ["run"]
