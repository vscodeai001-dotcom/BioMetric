FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["Payroll.Web/Payroll.Web.csproj", "Payroll.Web/"]
COPY ["Payroll.Shared/Payroll.Shared.csproj", "Payroll.Shared/"]

RUN dotnet restore "Payroll.Web/Payroll.Web.csproj"

COPY . .

WORKDIR "/src/Payroll.Web"
RUN dotnet publish "Payroll.Web.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://0.0.0.0:${PORT:-10000}
ENV ASPNETCORE_ENVIRONMENT=Production

COPY --from=build /app/publish .

EXPOSE 10000

ENTRYPOINT ["dotnet", "Payroll.Web.dll"]