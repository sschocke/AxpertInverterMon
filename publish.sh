#bin/sh

dotnet publish -r linux-arm /p:ShowLinkerSizeComparison=true 
rsync -r -v bin/Debug/net6.0/linux-arm/publish/ /home/pi/AxpertInverterMon/
