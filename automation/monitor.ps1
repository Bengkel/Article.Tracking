$iotHub = "<YOUR-IOT-HUB-NAME>"
#$subscription = "<YOUR-SUBSCRIPTION-ID>"

#az account set --subscription $subscription

#Monitor Device to Cloud (D2C) message
az iot hub monitor-events -n  $iotHub --properties anno sys --timeout 0