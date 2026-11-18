# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/LoanApplication.Api/LoanApplication.Api.csproj", "src/LoanApplication.Api/"]
COPY ["src/LoanApplication.Worker/LoanApplication.Worker.csproj", "src/LoanApplication.Worker/"]
RUN dotnet restore "src/LoanApplication.Worker/LoanApplication.Worker.csproj"
COPY . .
WORKDIR "/src/src/LoanApplication.Worker"
RUN dotnet publish "LoanApplication.Worker.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "LoanApplication.Worker.dll"]
