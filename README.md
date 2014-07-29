AzureQueueMulticaster
=====================

This solution allows each Azure Storage Queue message to be processed by multiple recipients,
similar to Azure ESB topics. 

The Azure worker role picks messages from one queue and posts them to multiple 
other queues for multiple handlers to process.

It's not limited to a single source queue - it can listen to multiple source queues 
and dispatch messages to multiple destination queues.

Don't forget to use Git to Update Submodules to bring in dependencies - Aspectacular framework,
which, BTW, has Azure Storage Queue helper class implementing both blocking wait for messages,
and async callback notification upon message arrival, enabling pub/sub pattern so sorely
missing in Azure Storage Queue functionality.

Enjoy!
