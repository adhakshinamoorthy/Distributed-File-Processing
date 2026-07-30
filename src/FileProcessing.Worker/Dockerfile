FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/FileProcessing.Worker/FileProcessing.Worker.csproj -c Release -o /app --no-self-contained
FROM mcr.microsoft.com/dotnet/runtime:10.0
USER root
RUN apt-get update && apt-get install -y --no-install-recommends tesseract-ocr tesseract-ocr-eng && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet","FileProcessing.Worker.dll"]
