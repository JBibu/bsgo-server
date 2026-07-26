# Toolchain for BSGO client analysis and server development.
# Everything lives in here: nothing is installed on the host.
FROM mcr.microsoft.com/dotnet/sdk:9.0

ENV DEBIAN_FRONTEND=noninteractive \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1

RUN apt-get update && apt-get install -y --no-install-recommends \
        python3 python3-pip python3-venv \
        git curl unzip xxd file jq less ripgrep \
    && rm -rf /var/lib/apt/lists/*

# --- .NET decompiler (ILSpy) ---------------------------------------------
# Installed on a global path so it works with any host UID.
ENV DOTNET_TOOLS=/opt/dotnet-tools
RUN dotnet tool install ilspycmd --version 9.0.0.7889 --tool-path $DOTNET_TOOLS
# ilspycmd 9.x targets net8.0 and only the 9.x runtime is present here.
ENV DOTNET_ROLL_FORWARD=LatestMajor

# --- Python tooling for Unity assets -------------------------------------
# UnityPy: reads the client's assetbundles / resources.assets.
RUN python3 -m venv /opt/venv \
    && /opt/venv/bin/pip install --no-cache-dir --upgrade pip \
    && /opt/venv/bin/pip install --no-cache-dir UnityPy

ENV PATH="/opt/venv/bin:${DOTNET_TOOLS}:${PATH}"

# Caches inside the workspace: the container runs as the host UID and $HOME
# (/root) is not writable for that user.
ENV DOTNET_CLI_HOME=/work/.container \
    NUGET_PACKAGES=/work/.container/nuget \
    XDG_DATA_HOME=/work/.container/share \
    HOME=/work/.container

WORKDIR /work
CMD ["sleep", "infinity"]
