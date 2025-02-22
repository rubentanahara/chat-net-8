# Start with the base ASP.NET runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
# Set the working directory in the container
WORKDIR /app
# Expose ports for HTTP and HTTPS
EXPOSE 80
EXPOSE 443

# Use SDK image for building the application
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
# Copy project files first to optimize layer caching
COPY ["src/API/ChatSystem.API/ChatSystem.API.csproj", "src/API/ChatSystem.API/"]
COPY ["src/Application/ChatSystem.Application/ChatSystem.Application.csproj", "src/Application/ChatSystem.Application/"]
COPY ["src/Domain/ChatSystem.Domain/ChatSystem.Domain.csproj", "src/Domain/ChatSystem.Domain/"]
COPY ["src/Infrastructure/ChatSystem.Infrastructure/ChatSystem.Infrastructure.csproj", "src/Infrastructure/ChatSystem.Infrastructure/"]
# Restore NuGet packages
RUN dotnet restore "src/API/ChatSystem.API/ChatSystem.API.csproj"
# Copy the rest of the source code
COPY . .
WORKDIR "/src/src/API/ChatSystem.API"
# Build the application
RUN dotnet build "ChatSystem.API.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
# Publish the application
RUN dotnet publish "ChatSystem.API.csproj" -c Release -o /app/publish

# Final stage
FROM base AS final
WORKDIR /app
# Copy published files from publish stage
COPY --from=publish /app/publish .
# Set the entry point for the container
ENTRYPOINT ["dotnet", "ChatSystem.API.dll"]