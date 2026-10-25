# 第三方声明 / Third-Party Notices

## CombatSolver

BlitzSolver 的部分源码移植自 CombatSolver，按其 MIT 许可证使用。

Portions of BlitzSolver are ported from CombatSolver and used under its MIT License.

- 项目 / Project：CombatSolver
- 仓库 / Repository：https://github.com/Torch1230/CombatSolver
- 移植范围 / Ported code：`src/Core/Search/Ported/`。每个文件开头注明源文件路径与源提交（`a62f1850`），只带过来 BlitzSolver 实际读取的成员。
  Each file names its source path and source commit (`a62f1850`) in its header and carries only the members BlitzSolver reads.

BlitzSolver 的搜索核心（`src/Core/Engine`、`src/Core/Content`、`src/Core/Search` 中移植目录以外的部分）最初由 rank-Yu 作为 CombatSolver 的可选后端 Flash 开发，提交历史随本仓库保留。

The search core outside the ported directory was originally written by rank-Yu as Flash, an optional CombatSolver backend; its commit history is preserved in this repository.

CombatSolver 许可证原文 / CombatSolver license:

```text
MIT License

Copyright (c) 2026 Torch1230 and contributors

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
```
