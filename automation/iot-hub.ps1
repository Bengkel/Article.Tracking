$resourceGroup = "<YOUR-RESOURCE-GROUP-NAME>"
$region="westeurope"
$iotHub = "<YOUR-IOT-HUB-NAME>"
$deviceId = "<YOUR-DEVICE-NAME>"
$secondsPerYear = 31556926

#Enbale when you have multiple subscription
#az account set --subscription "<YOUR-SUBSCRIPTION-ID>"

# Create the Resource Group
az group create --name $resourceGroup --location $region 

# Install IoT functionality Azure IoT CLI Extension.
az extension add --name azure-iot

# Create the IoT Hub
az iot hub create --name $iotHub --resource-group $resourceGroup --sku S1

# Create a device
az iot hub device-identity create -n $iotHub -d $deviceId

# Get device connection string
$deviceIdConnectionString = $(az iot hub device-identity connection-string show --device-id $deviceId --hub-name $iotHub | ConvertFrom-Json)
echo $deviceIdConnectionString.connectionString

# Generate a Device SAS token using a Device connection string
$deviceIdSasToken = $(az iot hub generate-sas-token --connection-string $deviceIdConnectionString.connectionString --duration $secondsPerYear | ConvertFrom-Json)
echo $deviceIdSasToken.sas 