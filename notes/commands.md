# Commands.  

## kubectl config get-clusters

- get connected clusters

## kubectl config current-context

- currently connected to

## kubectl top node

## kubectl top pods -A 


## kubectl run mynginx --image=nginx

- pulls and runs image

## kubectl port-forward mynginx <LOCALPORT:CONTAINERPORT>

## curl localhost:<LOCALPORT>

## kubectl exec -it mynginx -- sh

## kubectl apply -f manifest.yml


## kubectl get <RESOURCE>


## kubectl describe <RESOURCE> <RESOURCE_NAME>


## kubectl delete -f manifest.yml


## kubectl delete <RESOURCE> <RESOURCE_NAME>
 

## kubectl logs -n mytravels-default -l app=rabbitmq --tail=50


## kubectl create cm myconfigmap --from-file=config.txt


## kubectl get cm -A

# Labels

## kubectl get pods --show-labels 

## watch -n 0.1 kubectl get pods --show-labels

## kubectl label pod mynginx <KEY>=<VALUE>

## kubectl get pods, svc -n mytravels-default --selector=app=ai       

## kubectl get pods -n mytravels-default -l app=api


# Deployment

## kubectl create deployment mydeploy --image=nginx


## kubectl get deploy


## kubectl scale deploy mydeploy --replicas 3

