FROM mcr.microsoft.com/dotnet/sdk:10.0.201 AS build
ARG PROJECT
WORKDIR /repo
COPY . .
RUN dotnet publish samples/${PROJECT}/${PROJECT}.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
ARG PROJECT
ENV TELECOM_HOST=${PROJECT}
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["sh", "-c", "exec dotnet \"${TELECOM_HOST}.dll\" \"$@\"", "--"]
