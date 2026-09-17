One WebSocket shared by every feature of an application: one anonymous socket per client, a process
singleton owning all the connections, and a `{topic, message}` envelope that tells each feature which
frames are its own.

Features never open a socket or mount an endpoint. They inject the manager and push, to one client
through its connection or to all of them by broadcast. A feature working per connection subscribes
there and has nothing to unsubscribe on close. Topics form one flat namespace: name it after its
package - anything shorter eventually collides.

The socket carries no identity and no validator sees what arrives. Incoming frames are not decoded
until something subscribes.
