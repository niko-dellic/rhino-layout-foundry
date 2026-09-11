# Third-party dependencies

Foundry source is distributed under the repository's MIT license.

The distributable bundle contains Foundry's RHP, Core, UI, and Extensibility assemblies, the RhinoFoundry.UI shared assemblies, and Markdig.dll. RhinoCommon, Eto, Microsoft.macOS, System.Drawing, and the .NET runtime are supplied by the installed Rhino/runtime environment; the staging tool does not copy SDK or runtime dependencies into the bundle.

RhinoCommon SDK: Robert McNeel and Associates, copyright 1997–2025 as recorded in the pinned NuGet package metadata. Rhino and related trademarks belong to their owners. Users need a licensed, supported Rhino installation.

The NuGet dependency graph (including transitive build dependencies and test-only xUnit/Test SDK dependencies) is recorded in each project's packages.lock.json. Build and test-only packages are not shipped. Markdig 0.37.0 is a distributed runtime dependency (BSD-2-Clause); RhinoFoundry.UI 0.3.0-preview.37 is distributed under MIT. Review their upstream notices and vulnerability reports when changing the pinned graph. This document is an inventory, not a replacement for upstream licenses.

## Markdig 0.37.0 license

Source: https://github.com/xoofx/markdig/tree/1a1bbecc467a800dd6b39e68825df50309f6065c

Copyright (c) 2018-2019, Alexandre Mutel
All rights reserved.

Redistribution and use in source and binary forms, with or without modification
, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

## RhinoFoundry.UI license

MIT License

Copyright (c) 2026 niko-dellic

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
