# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/LoanApplication.Api/LoanApplication.Api.csproj", "src/LoanApplication.Api/"]
RUN dotnet restore "src/LoanApplication.Api/LoanApplication.Api.csproj"
COPY . .
WORKDIR "/src/src/LoanApplication.Api"
RUN dotnet publish "LoanApplication.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "LoanApplication.Api.dll"]