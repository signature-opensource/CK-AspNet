One WebSocket shared by every feature of an application: one anonymous socket per client, a process
singleton owning all the connections, and a `{topic, message}` envelope that tells each feature which
frames are its own.

Features never open a socket or mount an endpoint. They inject the manager and push - to one client
through its connection, or to every client at once. A feature working per connection subscribes on
that connection and has nothing to unsubscribe when it closes. Topics form one flat global namespace:
name it after its package or it collides.

The socket carries no identity, and nothing on it is validated. Incoming frames are not even
decoded until something subscribes.
