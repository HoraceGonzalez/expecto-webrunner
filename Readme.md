# Expecto Web-based Test Runner

## Setup

## Build

To run a complete build, run the command below (WINDOWS). Note a full build with default configs requires a connection over VPN to Orchard server playgound_rs

```
./build
```

On Linux/Mac (Use version of mono compatible with 4.8.1):

```
./build.sh
```

Additional build targets:

Build in "debug" mode with incremental build.


```
./build debug
```

The following does a release build (without unit tests)

```
./build copybinaries
```

*Note on Debug Builds: sometimes you'll notice after pulling down a new bizapps branch that `./build debug` may fail while `/build` seems to work just fine. The likely explanation is that
the there are some stale binaries left over in the various `src/**/bin` and `test/**/bin` directories from your last `./build debug` build. It's important to note that that
"debug" builds are incremental (so they're faster), and they'll reuse compiled binaries for code that hasn't changed. These need to be "cleaned" so the F#
compiler will rebuild the binaries from the new branch's code from scratch rather than attempt to reuse the binaries from the stale build. 

To run a clean debug build from scratch, use:

```
./build debug clean
```

## Running

To start the api server, run the command below

```
./build start
```

If you'd like to build from scratch, use: 

```
./build start clean
```

### Scripting Environment

The scripting environment depends on some conveniently generated paket dependency include scripts.
You can generate these from the command line by doing a normal "debug" build. Eg.

```
./build debug
```

You can explicitly generate the scripts (without doing a debug build) by running the following build target 
(Note: `.paket/paket install` may be required):

```
./build GeneratePaketLoadScripts
```

You can generate the scripts directly using the following paket commands (these are included in the "GeneratePaketLoadScripts" target for convenience)

```
.paket\paket.exe install
.paket\paket.exe generate-load-scripts framework net461 type fsx
```

On Linux:


```
mono .paket/paket.exe install
mono .paket/paket.exe generate-load-scripts framework net461 type fsx

```