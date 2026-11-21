# EchoRendering

The native Swift module that loads Echo-exported GLBs, builds SceneKit geometry and fits the camera to model bounds. EchoDesktop uses these features in its viewers. EchoRender uses them to produce PNG snapshots with Metal in a windowless process.

EchoRender reads one JSON render request from stdin and returns one JSON result on stdout. Errors go to stderr. The C# CLI validates the result and publishes the PNG after the worker succeeds.
