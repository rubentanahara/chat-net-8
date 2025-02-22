# Start with the base .NET runtime image (smaller than SDK)
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS base
WORKDIR /app

# Use SDK image for building the application
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
# Copy project file first to optimize layer caching
COPY ["src/RequestorUI/ChatSystem.RequestorUI/ChatSystem.RequestorUI.csproj", "src/RequestorUI/ChatSystem.RequestorUI/"]
# Restore NuGet packages
RUN dotnet restore "src/RequestorUI/ChatSystem.RequestorUI/ChatSystem.RequestorUI.csproj"
# Copy the rest of the source code
COPY . .
WORKDIR "/src/src/RequestorUI/ChatSystem.RequestorUI"
# Build the application
RUN dotnet build "ChatSystem.RequestorUI.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
# Publish the application
RUN dotnet publish "ChatSystem.RequestorUI.csproj" -c Release -o /app/publish

# Final stage
FROM base AS final
WORKDIR /app
# Copy published files from publish stage
COPY --from=publish /app/publish .
# Set the entry point for the container
ENTRYPOINT ["dotnet", "ChatSystem.RequestorUI.dll"]