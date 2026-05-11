# 功能管理框架（纯 C# 功能包 + 双总线 + 组合根）设计规格

**日期**：2026-05-11  
**状态**：待你评审（评审通过后进入 `writing-plans` 实现计划）  
**范围**：`Src/frame` 内框架契约与默认实现、启动编排约定、全局/内部事件与类型注册规则；不含具体业务功能包实现。

---

## 1. 目标与约束

### 1.1 目标

- 每个**功能**以独立**功能包**（纯 C#）交付：**对内聚合**实现细节，**对外仅依赖 `frame` 抽象与契约类型**，功能包之间**禁止直接项目引用**。
- 提供**工厂/组合根**：按**显式手写清单**顺序执行各包的安装入口。
- 提供**全局管理器**（非 Godot `Node`）：持有已注册包、统一 **初始化 / 关闭**（与进程同寿命）。
- **事件**：每个功能包拥有**独立内部事件总线**；另有**全局事件总线**供跨包协作。**仅当多个功能参与同一协作链时使用全局总线**，单包内通信只走内部总线。
- **全局事件定型**：通道键使用**弱类型路由**（推荐 `frame` 内单一 `GlobalEventId` 枚举）；每个事件 id 在启动安装阶段**注册唯一载荷类型**（`record`/DTO）。`Publish`/`Subscribe` 的泛型与注册表不一致时在开发配置下**失败可见**。

### 1.2 约束（已与你确认）

- 功能形态：**A** — 以纯 C# 服务为主，不绑定专属场景子树。
- 管理器形态：**D** — 管理器为普通类或静态入口；由启动代码（如 `MainRoot` 或专用 `GameBootstrap`）驱动；可选极薄 Autoload 仅转发到同一组合根（本规格不强制 Autoload）。
- 包间依赖：**A** — 不允许包间直接引用；跨包仅通过**全局总线**及**共享契约类型**（见 2.3）。
- 全局事件 id：**单一枚举**置于 `frame`（或与 DTO 同区的契约命名空间），避免多程序集各自枚举导致键冲突；各包仅登记本包使用子集。
- 枚举 ↔ 类型映射：**A** — 各包在 `Install` 中**自注册**本会发布的全局事件 schema。
- 功能发现：**A** — **显式清单**，不使用反射扫描；清单顺序即 **Install 顺序**。
- 生命周期：**A** — **进程级单例**，启动一次、退出释放；**Shutdown 与 Install 顺序相反**。

---

## 2. 架构与组件

### 2.1 分层与目录（建议）

| 位置 | 内容 |
|------|------|
| `Src/frame/featurekit/`（名称可微调） | `IFeaturePackage`、`IFeatureCompositionContext`、`FeatureManager`、`FeatureBootstrap`、默认 `GlobalEventBus` / `InternalEventBus` 实现、`GlobalEventId` 与**跨包共享 DTO**（仅契约，无业务互引） |
| `Src/mod/<FeatureName>/` | 各 `XxxFeaturePackage : IFeaturePackage` 及包内私有类型 |
| 启动清单 | `MainRoot` 或 `Src/frame/GameBootstrap.cs` 中维护 `IReadOnlyList<IFeaturePackage>` |

### 2.2 接口职责

- **`IFeaturePackage`**  
  - `void Install(IFeatureCompositionContext ctx);`  
  - 职责：注册本包的全局事件 schema、订阅全局事件、获取本包专用 `IInternalEventBus`、向 `FeatureManager` 登记本包可查询的对外门面（若有）。**禁止**在此获取其他功能包的具体类型实例。

- **`IFeatureCompositionContext`**（窄接口，组合根注入）  
  - 暴露：`IGlobalEventBus`、`IInternalEventBus CreateInternalBus()`（或等价「每包一实例」工厂）、`FeatureManager`（只读或受限 API）、以及后续经评审同意的少量横切能力（日志等）。  
  - **不**提供通用 `Resolve<T>()` 式万能定位器，除非后续规格明确扩展。

- **`FeatureBootstrap`（或同名工厂）**  
  - 输入：包列表、已构造的 `FeatureCompositionContext` 实现。  
  - 行为：按序 `Install`；全部完成后执行 **schema 校验**（见第 4 节）；最后调用 `FeatureManager.Initialize()`（若需要二阶段初始化，在本规格中合并为「Install 结束即就绪」，除非实现计划拆分）。

- **`FeatureManager`**  
  - 保存包实例引用；可选 `T GetPackage<T>()` **仅限本进程调试或 `frame` 内编排**（默认不鼓励业务代码依赖）；对外业务仍应以事件与契约为界。  
  - `Shutdown()`：按与 **Install 相反顺序** 调用各包约定的释放钩子（见 2.4）。

- **`IGlobalEventBus`**  
  - `RegisterSchema(GlobalEventId id, Type payloadType)`（或泛型重载）；`Publish`/`Subscribe` 与 schema 绑定。  
  - 合并多包注册：**同一 id 仅允许一种载荷类型**；第二次注册类型不一致 → **启动失败**。

- **`IInternalEventBus`**  
  - 每包独立实例；API 形态可与全局总线类似但**不**经过全局 schema 表（包内可用独立枚举或 POCO，不在本规格强制）。

### 2.3 契约与「零包间引用」

- **全局事件载荷 DTO** 与 **`GlobalEventId`**：放在 `frame` 的契约区（例如 `Src/frame/featurekit/Contracts/`），任何发布/订阅该 id 的包只引用 `frame`。  
- **禁止**：功能包 A 直接 `using Mod.B` 或引用 B 的程序集。  
- **允许**：多个包引用同一 `frame` 契约类型；B 订阅 A 通过全局总线发出的、定义在 `frame` 的 DTO。

### 2.4 功能包释放约定

- 若包持有订阅、定时器、文件句柄等：在 `IFeaturePackage` 上增加 `void Shutdown(IFeatureCompositionContext ctx)`（或在管理器统一接口中约定），由 `FeatureManager.Shutdown()` **逆序**调用。  
- 内部总线在包 `Shutdown` 时**清空订阅**并置为不可再发布（实现细节由实现计划规定）。

---

## 3. 数据流

1. 启动代码构造：`GlobalEventBus`、`FeatureManager`、具体 `FeatureCompositionContext`。  
2. `FeatureBootstrap.Run(packages)`：对列表中每个 `IFeaturePackage` 调用 `Install`。  
3. 在每个 `Install` 内：  
   - 先 `RegisterSchema` 本会 **Publish** 的全局 id；  
   - 再 `Subscribe` 需要消费的全局 id；  
   - 包内模块仅使用 **本包 `IInternalEventBus`**。  
4. 跨包协作：发布方 `GlobalBus.Publish(id, dto)`；订阅方仅依赖 dto 类型定义所在程序集（`frame`）。  
5. 进程退出：`FeatureManager.Shutdown()` 逆序释放 → 全局总线取消所有订阅。

---

## 4. 错误处理与诊断

| 场景 | 行为 |
|------|------|
| 同一 `GlobalEventId` 重复注册且 `payloadType` 不同 | 启动阶段抛异常，消息包含冲突包名或注册来源（实现计划定具体类型名）。 |
| `Publish`/`Subscribe` 使用的泛型与已注册 schema 不一致 | 开发构建：抛异常或断言失败；是否提供「宽松模式」由实现计划决定，本规格默认**严格**。 |
| 包在 `Install` 中直接引用另一包程序集 | 工程与审查层面禁止；本规格不要求编译器强制分程序集，但推荐后续按包拆 csproj 以硬化边界。 |

---

## 5. 测试策略（框架级）

- 使用 **Fake** `IGlobalEventBus` / `IFeatureCompositionContext` 对单包 `Install` 做单元测试。  
- 集成测试：注册两个最小桩包，A 发布全局事件，B 订阅，断言载荷类型与调用次数。  
- 冲突测试：两个包对同一 id 注册不同载荷类型 → 期望启动失败。

---

## 6. 非目标（YAGNI）

- 不在本规格内定义：按游戏模式动态卸载功能包、热重载、反射自动发现、完整 DI 容器。  
- 不在本规格内绑定：具体 Roguelike 业务管理器（与另文 `kemo-card-co-op-roguelike` 规格独立，可在实现阶段对接）。

---

## 7. 自检记录（占位扫描）

- 无 `TBD` / `TODO` 未完成段；歧义点：**`FeatureManager.GetPackage<T>`** 标为可选且默认不鼓励业务使用——若你希望完全删除该 API，可在评审意见中说明，实现计划将改为仅内部测试钩子。
