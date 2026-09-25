FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY desktop/tools/VjcRelay/VjcRelay.csproj tools/VjcRelay/
RUN dotnet restore tools/VjcRelay/VjcRelay.csproj
COPY desktop/tools/VjcRelay/ tools/VjcRelay/
RUN dotnet publish tools/VjcRelay/VjcRelay.csproj -c Release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY --from=build /out ./
EXPOSE 8722/udp
ENTRYPOINT ["dotnet", "/app/VjcRelay.dll", "8722"]
