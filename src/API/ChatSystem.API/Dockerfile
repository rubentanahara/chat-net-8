FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project files
COPY ["src/API/ChatSystem.API/ChatSystem.API.csproj", "src/API/ChatSystem.API/"]
COPY ["src/Domain/ChatSystem.Domain/ChatSystem.Domain.csproj", "src/Domain/ChatSystem.Domain/"]
COPY ["src/Application/ChatSystem.Application/ChatSystem.Application.csproj", "src/Application/ChatSystem.Application/"]
COPY ["src/Infrastructure/ChatSystem.Infrastructure/ChatSystem.Infrastructure.csproj", "src/Infrastructure/ChatSystem.Infrastructure/"]

# Restore dependencies
RUN dotnet restore "src/API/ChatSystem.API/ChatSystem.API.csproj"

# Copy the rest of the code
COPY . .

# Build the application
RUN dotnet build "src/API/ChatSystem.API/ChatSystem.API.csproj" -c Release -o /app/build

# Publish the application
FROM build AS publish
RUN dotnet publish "src/API/ChatSystem.API/ChatSystem.API.csproj" -c Release -o /app/publish

# Final stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Install curl for health checks
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

COPY --from=publish /app/publish .
EXPOSE 80
EXPOSE 443

ENTRYPOINT ["dotnet", "ChatSystem.API.dll"]