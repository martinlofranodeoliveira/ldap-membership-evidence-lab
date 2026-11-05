FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:de4fe90a98845d75910b9769662465c84de1a934665c431279d1382b2bddc02b
ENV DEBIAN_FRONTEND=noninteractive \
    NUGET_PACKAGES=/work/build/packages \
    DOTNET_CLI_HOME=/work/build/dotnet-home \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_WORKLOAD_UPDATE_NOTIFY_DISABLE=true \
    DOTNET_CLI_USE_MSBUILD_SERVER=0 \
    DOTNET_PROCESSOR_COUNT=2 \
    DOTNET_GCHeapHardLimit=0x30000000 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
RUN printf '#!/bin/sh\nexit 101\n' > /usr/sbin/policy-rc.d && chmod 755 /usr/sbin/policy-rc.d
RUN printf 'slapd slapd/no_configuration boolean true\n' | debconf-set-selections
RUN apt-get update && apt-get install -y --no-install-recommends slapd ldap-utils gcc libc6-dev
WORKDIR /work
COPY . /work
RUN dotnet --info && dpkg-query -W slapd ldap-utils gcc libc6 && dotnet restore MembershipEvidence.sln --locked-mode --disable-parallel --disable-build-servers && dotnet build MembershipEvidence.sln --no-restore --configuration Release
RUN mkdir -p /work/build/dotnet-home && chown -R 1000:1000 /work
USER 1000:1000
CMD ["dotnet", "test", "--solution", "MembershipEvidence.sln", "--no-restore", "--configuration", "Release"]
