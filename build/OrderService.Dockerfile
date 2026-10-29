FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo
COPY src/Pizza.OrderService/Pizza.OrderService.csproj src/Pizza.OrderService/
RUN dotnet restore src/Pizza.OrderService/Pizza.OrderService.csproj
COPY src/Pizza.OrderService/ src/Pizza.OrderService/
RUN dotnet publish src/Pizza.OrderService/Pizza.OrderService.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Pizza.OrderService.dll"]
