# MXEngine — Code Review

> **Bản đánh giá lịch sử tại commit 9b5ef10.** Các tên API và line number dưới đây mô tả source trước đợt refactor. Tình trạng hiện tại xem [README](../README.MD). F01–F07, F10–F14 đã có sửa đổi trong worktree; F08 được quy định bằng ownership contract, F09 được giữ theo phạm vi refactor. Play Mode và test Addressables thật cần chạy lại trước khi coi các sửa đổi đã được xác nhận runtime.

> Review source tại commit `9b5ef10`, tập trung `Assets/MXEngine`, sample Navigation Demo và `Packages/manifest.json`. Đây là **static review**: chưa chạy Unity Play Mode, Addressables build, device build hoặc fault-injection test. Không sửa source và không tạo commit. `Confirmed issue` nghĩa là nhánh logic/thiếu API chứng minh được từ source **khi điều kiện nêu ra xảy ra**; `Potential issue` nghĩa là tác động còn phụ thuộc prefab, Unity runtime hoặc cách game dùng API.

## Executive Summary

MXEngine hiện là một khung **MVP cho UI prefab**: `NavigationService` tra `ViewCatalog`, nhờ `IViewLoader` tạo View, tạo `Presentr`, bind `ViewState`, rồi giữ Screen/Modal/Overlay và giải phóng theo thứ tự ngược. Đây là một lõi nhỏ, dễ lần theo, chưa phải hệ thống domain Model hoặc data binding hoàn chỉnh.

**Điểm tốt nhất:** đường mở thông thường có kiểm tra catalog/layer/reference; `Presentr` từ chối initialize lặp, chờ initialize đang chạy trước khi dispose; `NavigationService.ReleaseAsync` vẫn release View trong `finally` nếu Presenter teardown lỗi. Loader kiểm tra `AsyncOperationStatus`, null/result component và release handle trên nhánh load lỗi. Các điểm này nằm ở [`NavigationService`](../Assets/MXEngine/NavigationService.cs), [`Presenter.cs`](../Assets/MXEngine/MVP/Presenter.cs), [`AddressableViewLoader.cs`](../Assets/MXEngine/MVP/AddressableViewLoader.cs).

**Vấn đề lớn nhất:** `_gate` được giữ qua các hook async do game viết, nên `await` navigation lồng nhau trên cùng service gây deadlock; service không có shutdown API cho các UI còn mở; `stack: false` có thể mất một phần lịch sử khi release Screen cũ lỗi. Đây là rủi ro correctness cần xử lý trước khi dùng rộng trong production. Technical debt đáng chú ý là contract loader vẫn gắn `AssetReferenceGameObject`, State không có cơ chế cập nhật View, và ownership bị mờ vì caller nhận Presenter có `DisposeAsync()` công khai.

### Bảng phát hiện ưu tiên

Mức độ: **Critical** = lỗi chắc chắn phá luồng chính/không có cách phục hồi; **High** = deadlock, mất trạng thái hoặc resource lifecycle ở một đường sử dụng hợp lệ; **Medium** = lỗi theo điều kiện hoặc nợ kiến trúc ảnh hưởng đáng kể; **Low** = rủi ro cục bộ; **Nitpick** = naming/style. Không phát hiện Critical vô điều kiện trong source hiện tại.

| Severity | File | Issue | Impact | Recommendation |
| --- | --- | --- | --- | --- |
| High | [`NavigationService.cs` L40, L304, L327](../Assets/MXEngine/NavigationService.cs#L40) | **F01 · Confirmed issue:** hook async bind/init/unbind `await` navigation khác trên cùng service trong lúc `_gate` bị giữ. | Hai operation chờ nhau vô hạn; UI đứng. | Quy định và thực thi cơ chế không reentrant; tách hook khỏi critical section hoặc thiết kế queue/transition rõ ràng. |
| High | [`NavigationService.cs` L11–24](../Assets/MXEngine/NavigationService.cs#L11), [`NavigationDemo.cs` L72–81](../Assets/MXEngine/Samples/NavigationDemo/NavigationDemo.cs#L72) | **F02 · Confirmed issue:** không có shutdown/close-all Screen và owner sample không gọi teardown navigation. | Presenter/State không được dispose khi owner kết thúc; cleanup app tự đăng ký có thể sót. | Thêm `DisposeAsync`/`ShutdownAsync` cho service và await từ owner. |
| High | [`NavigationService.cs` L81–96](../Assets/MXEngine/NavigationService.cs#L81) | **F03 · Confirmed issue:** replace `stack:false` pop Screen cũ trước khi await release; release lỗi gây partial commit. | Lịch sử bị mất một phần dù replace thất bại. | Định nghĩa semantics khi teardown lỗi; giữ snapshot, cleanup hết và báo lỗi tổng hợp hoặc commit trạng thái nhất quán. |
| Medium | [`NavigationService.cs` L40, L148–180](../Assets/MXEngine/NavigationService.cs#L40) | **F04 · Confirmed issue:** cùng gate bao cả load Screen lẫn ShowOverlay. | Loading overlay yêu cầu sau khi load bắt đầu chỉ hiện sau khi load kết thúc. | Cho phép progress overlay/transition ngoài gate dài, hoặc mở overlay trước request và quản lý rõ ràng. |
| Medium | [`NavigationService.cs` L296–304](../Assets/MXEngine/NavigationService.cs#L296) | **F05 · Potential issue:** prefab được instantiate trước `SetActive(false)` và bind. | Prefab active có thể chạy `Awake`/`OnEnable` với State null hoặc lộ một frame. | Đặt trạng thái inactive ngay trong loader/instantiation contract; test prefab active trong Play Mode. |
| Medium | [`Presenter.cs` L48–53](../Assets/MXEngine/MVP/Presenter.cs#L48), [`NavigationService.cs` L307–318](../Assets/MXEngine/NavigationService.cs#L307) | **F06 · Confirmed issue:** cleanup ném lỗi có thể thay thế exception khởi tạo ban đầu. | Chẩn đoán lỗi gốc khó; caller chỉ thấy lỗi teardown. | Giữ primary exception và ghi/aggregate cleanup exception. |
| Medium | [`NavigationService.cs` L232–239](../Assets/MXEngine/NavigationService.cs#L232) | **F07 · Confirmed issue:** `CloseAllModalsAsync` dừng ở modal đầu tiên release lỗi. | Modal còn lại vẫn mở dù caller đã yêu cầu đóng tất cả. | Tiếp tục cleanup từng modal, thu lỗi và ném sau cùng. |
| Medium | [`Presenter.cs` L65–79](../Assets/MXEngine/MVP/Presenter.cs#L65), [`NavigationService.cs` L33–44](../Assets/MXEngine/NavigationService.cs#L33) | **F08 · Potential issue:** Presenter trả cho caller có thể bị dispose trực tiếp trong khi service vẫn giữ `OpenView`. | View còn trong stack và có thể active nhưng State đã unbind/dispose. | Làm rõ ownership; ưu tiên đóng qua service, hạn chế public disposal từ caller nếu API cho phép. |
| Medium | [`IViewLoader.cs` L7–14](../Assets/MXEngine/MVP/IViewLoader.cs#L7), [`ViewEntry.cs` L6–10](../Assets/MXEngine/MVP/ViewEntry.cs#L6) | **F09 · Confirmed issue:** interface loader và catalog vẫn dùng `AssetReferenceGameObject`. | Fake loader làm được trong Unity test, nhưng đổi backend/pooling cần sửa contract và asset schema. | Nếu có nhu cầu backend khác, dùng key/descriptor trung lập và ownership token. |
| Medium | [`NavigationService.cs` L40, L171](../Assets/MXEngine/NavigationService.cs#L40) | **F10 · Confirmed issue:** không có cancellation cho load/init/request đang đợi gate. | Request cũ có thể hoàn tất sau khi caller/scene không còn cần UI; gate bị giữ lâu. | Đưa `CancellationToken` qua service, loader, hook; định nghĩa rollback sau cancel. |
| Low | [`ViewCatalog.cs` L11–24](../Assets/MXEngine/MVP/ViewCatalog.cs#L11) | **F11 · Confirmed issue:** duplicate `ViewId` không được phát hiện, `Get` luôn chọn entry đầu. | Catalog cấu hình sai có thể mở nhầm prefab/layer hoặc fail runtime. | Validate ID duy nhất trong Editor/on enable; báo cả hai entry lỗi. |
| Low | [`ViewState.cs` L8–13](../Assets/MXEngine/MVP/ViewState.cs#L8) | **F12 · Confirmed issue:** `_disposed` đặt sau `OnDispose()`. | Hook ném lỗi khiến lần Dispose sau chạy hook lần nữa. | Đánh dấu terminal trước khi gọi hook và vẫn báo exception. |
| Low | [`AddressableViewLoader.cs` L32–37](../Assets/MXEngine/MVP/AddressableViewLoader.cs#L32) | **F13 · Potential issue:** không kiểm tra kết quả `ReleaseInstance`; bỏ qua nếu Unity `Component` đã bị destroy. | Không biết release thất bại; object/handle có thể không được xử lý đúng trong đường dùng sai. | Ghi nhận handle/ownership khi load và xác minh release, thêm test destroy ngoại tuyến. |
| Nitpick | [`Presenter.cs` L6](../Assets/MXEngine/MVP/Presenter.cs#L6) | **F14 · Confirmed issue:** tên public `Presentr` thiếu `e`. | API khó đọc và tìm kiếm. | Đổi tên khi có kế hoạch migration; không đổi chỉ để sửa style giữa chừng. |

## 1. Đánh giá kiến trúc tổng thể

**Pattern thực tế:** UI navigation service + một biến thể MVP. `View<TState>` là `MonoBehaviour`; `Presentr<TView,TState>` tạo State và gọi hook; `NavigationService` sở hữu thời gian sống các instance. `ScreenPresenter` và `ModalPresenter` hiện chỉ là marker bằng generic inheritance. Overlay có thể không có Presenter. Vì `ViewState` không chứa domain abstraction hay cơ chế notify, gọi đây là *MVP UI skeleton* chính xác hơn gọi là toàn bộ kiến trúc game.

**Separation of Concerns:** View giữ reference Unity và bind/unbind; Presenter lo lifecycle của State và logic trong hook; service điều hướng và load. Tách này hữu ích. Ranh giới còn mờ ở hai chỗ: `NavigationService` vừa quyết định stack/layer, vừa điều phối async construction/teardown và xác thực Addressable; `IViewLoader` tuy là interface nhưng vẫn lộ type Addressables. Sample còn nối button trực tiếp với navigation, không minh họa presenter xử lý event nghiệp vụ.

**Dependency direction:** Presenter không phụ thuộc NavigationService; NavigationService phụ thuộc `IViewLoader`, tốt cho thay loader. Nhưng service vẫn phụ thuộc `ViewCatalog`/`UIRoot` cụ thể và `ViewEntry.Reference`; interface chưa che Addressables. Đây là trade-off chấp nhận được cho lõi nhỏ, không phải vi phạm bắt buộc phải refactor ngay. Không thấy DI framework hoặc event bus ẩn trong source.

**Responsibility/over-engineering:** `NavigationService` dài 351 dòng và đã giữ nhiều responsibility; nếu thêm animation/cancellation/pooling vào cùng class sẽ khó bảo toàn state. `UniTaskCompletionSource` trong Presenter có lý do thật: chờ init và làm disposal idempotent. Hai marker Presenter và ba collection cũng có mục đích rõ; chưa có bằng chứng chúng là over-engineering. `ViewCatalog.Get` tuyến tính với vài chục entry chưa phải bottleneck đáng ưu tiên.

## 2. Review từng class quan trọng

| Class | Responsibility và điểm tốt | Vấn đề có căn cứ; severity |
| --- | --- | --- |
| [`NavigationService`](../Assets/MXEngine/NavigationService.cs) | Tạo/đóng UI, quản lý 3 layer, validate catalog, tuần tự hóa thao tác. Có cleanup `finally`, giữ Screen cũ để Back và tránh tạo lại Screen cùng ID. | F01–F04, F07, F10 (**High–Medium**). Không có shutdown; gate bao hook của code game; replace có nhánh partial commit. Đây là class có nhiều vai trò nhất. |
| [`View<TState>`](../Assets/MXEngine/MVP/View.cs) | Nắm component Unity và State; `UnbindAsync` xóa State trong `finally`, tốt khi hook lỗi. | **Potential issue · Low:** `BindAsync` public không chặn null hoặc bind lặp; caller ngoài service có thể overwrite State mà không unbind State cũ (L10–13). Base class không tự báo thay đổi State; đây là giới hạn API, chưa phải bug. |
| [`ViewState`](../Assets/MXEngine/MVP/ViewState.cs) | State theo lần mở, hook dispose nhỏ, dễ kế thừa. | F12 (**Low**). Không có data notification; nếu cần reactive UI, game phải tự cài. |
| [`Presentr<TView,TState>`](../Assets/MXEngine/MVP/Presenter.cs) | Tạo State, bind → initialize; disposal đợi init, unbind → `OnDispose` → State.Dispose; init/dispose lặp được xử lý. | F06, F08, F12 liên quan, F14 (**Medium–Nitpick**). `Dispose()` dùng `.Forget()` không cho caller chờ; tự `await DisposeAsync()` từ chính hook init sẽ tự chờ `_initializationCompletion` và deadlock (**Potential issue · Medium**, L37–45, L86–88). Constraint `new()` hạn chế DI vào State. |
| [`ScreenPresenter`](../Assets/MXEngine/MVP/ScreenPresenter.cs) | Type marker để `ShowScreenAsync` compile-time chỉ nhận Screen presenter. | Chưa có lifecycle riêng; không tìm thấy defect độc lập (**Low/API trade-off**). Nếu sau này cần screen-specific behavior, nên xác định rõ contract thay vì thêm override tùy tiện. |
| [`ModalPresenter`](../Assets/MXEngine/MVP/ModalPresenter.cs) | Marker tương tự cho `ShowModalAsync`. | Không có close callback/behavior riêng; đóng Modal là trách nhiệm caller/service (**Low/API trade-off**). |
| [`IViewLoader`](../Assets/MXEngine/MVP/IViewLoader.cs) | Hợp đồng load/release nhỏ, cho phép fake loader trong Unity test. | F09 (**Medium**). Không trả ownership handle; `ReleaseAsync(Component)` giả định có thể tìm đúng instance từ component. |
| [`AddressableViewLoader`](../Assets/MXEngine/MVP/AddressableViewLoader.cs) | Kiểm status, result, root component; release handle nếu load lỗi; dùng `ReleaseInstance` khi đóng. | F13 (**Low/Potential**). `LoadAsync` yêu cầu component trên root, không tìm child; đây là contract cần ghi rõ. Không tìm thấy lỗi double release ở đường service thông thường. |
| [`ViewCatalog`](../Assets/MXEngine/MVP/ViewCatalog.cs) | ScriptableObject để cấu hình ID → entry; `Get` null-safe với list/entry. | F11 (**Low**). Tìm tuyến tính là đủ cho quy mô hiện tại; vấn đề thực là thiếu validation duplicate/invalid entry trước runtime. |
| [`ViewEntry`](../Assets/MXEngine/MVP/ViewEntry.cs) | Dữ liệu ID, Addressable reference, layer rõ ràng. | `public` mutable fields cho phép thay runtime mà service không revalidate entry đã mở (**Potential issue · Low**); gắn trực tiếp Addressables (F09). |
| [`ViewId`](../Assets/MXEngine/MVP/ViewId.cs) | Enum cho call site typed, không dùng string magic. | Thêm screen mới cần sửa enum và catalog; có thể va chạm quy trình khi nhiều module/game mở rộng (**Future concern · Low**), chưa phải bug. |
| [`UIRoot`](../Assets/MXEngine/UIRoot.cs) | Ba parent serialize rõ ràng; service kiểm tra null ở lúc mở. | Không validate toàn bộ root lúc bootstrap; lỗi cấu hình chỉ xuất hiện khi mở layer tương ứng (**Potential issue · Low**). |

Sample [`NavigationDemo`](../Assets/MXEngine/Samples/NavigationDemo/NavigationDemo.cs) tạo service và catch exception quanh mỗi action, là ví dụ API có ích. Nó không teardown service trong `OnDestroy` (F02). `_busy` bỏ qua click trong lúc chuyển màn hình; đó là chính sách demo, không phải behavior bắt buộc của service. [`NavigationDemoView`](../Assets/MXEngine/Samples/NavigationDemo/NavigationDemoView.cs) bind rỗng nên không chứng minh được đường event View ↔ Presenter trong game thật.

## 3. Lifecycle: đường bình thường và các nhánh lỗi

```mermaid
sequenceDiagram
    actor Caller
    participant Nav as NavigationService
    participant Loader as IViewLoader
    participant Addr as Addressables
    participant View
    participant P as Presentr
    participant State as ViewState
    Caller->>Nav: ShowScreenAsync(id, factory)
    Nav->>Nav: await gate; GetEntry + validate layer/ref
    Nav->>Loader: LoadAsync(reference, ScreenRoot)
    Loader->>Addr: InstantiateAsync(parent)
    Addr-->>Loader: GameObject + handle
    Loader-->>Nav: root TView
    Nav->>View: SetActive(false)
    Nav->>P: factory(view); configure?
    Nav->>P: InitializeAsync()
    P->>State: new TState()
    P->>View: BindAsync(state)
    P->>P: OnInitializeAsync(state)
    P-->>Nav: initialized
    Nav->>View: SetActive(true); push
    Nav-->>Caller: presenter
    Caller->>Nav: Back / Close / Hide
    Nav->>P: await DisposeAsync()
    P->>View: await UnbindAsync()
    P->>P: OnDispose()
    P->>State: Dispose()
    Nav->>Loader: ReleaseAsync(view)
    Loader->>Addr: ReleaseInstance(gameObject)
```

| Trường hợp cần kiểm tra | Kết luận từ source |
| --- | --- |
| Double initialize | **Đã chặn:** `_initialized` hoặc `_initializationCompletion != null` ném `InvalidOperationException` (`Presenter.cs` L22–30). |
| Double dispose | **Đã chặn ở Presenter:** cùng `_disposeCompletion.Task` được trả cho lần gọi sau (L71–79). `ViewState` còn F12 nếu `OnDispose` ném. |
| Dispose khi init chưa xong | **Được chờ:** `DisposeCoreAsync` await `_initializationCompletion` trước khi unbind/dispose State (L82–105). Nếu chính hook init await disposal của nó thì có chu trình chờ; tránh reentrant await. |
| Lỗi khi load hoặc thiếu component | **Có cleanup:** `AddressableViewLoader` kiểm handle status/result/T và release handle valid ở catch (L12–29). |
| Lỗi factory/configure/init | **Có cleanup:** `CreateAsync` dispose Presenter nếu đã tạo và release View trong `finally` (`NavigationService.cs` L297–319). F06 còn làm lỗi gốc bị che khi cleanup lỗi. |
| Lỗi khi đóng | **Có release cố gắng:** `ReleaseAsync` dùng `finally` để gọi loader dù Presenter dispose lỗi (L322–333). F03/F07 là vấn đề state sau lỗi đó. |
| View bị destroy ngoài service | **Potential issue:** `OpenView` còn reference đến Presenter/View; lời gọi `view.gameObject` hoặc `SetActive` sau đó có thể lỗi, `ReleaseAsync` của Addressables skip Unity-null view (L32–35). Không có callback đồng bộ registry khi GameObject bị destroy. |
| Presenter bị caller dispose ngoài service | **Potential issue:** service không biết; View có thể vẫn active/in stack nhưng đã unbind và State disposed (F08). |
| State dispose quá sớm | **Không thấy ở đường service bình thường:** service await Presenter trước release; Presenter chờ init trước dispose State. |
| Addressables release sai | **Không thấy ở đường bình thường:** loader dùng `ReleaseInstance` cho instance nó tạo; thiếu kiểm tra kết quả và trường hợp destroy bên ngoài là F13. |

Việc `Addressables.ReleaseInstance` là cách release instance được Addressables theo dõi phù hợp với tài liệu [Unity Addressables 2.9.1](https://docs.unity.cn/Packages/com.unity.addressables@2.9/api/UnityEngine.AddressableAssets.Addressables.ReleaseInstance.html). Không kết luận scene unload **chắc chắn** làm rò Addressables handle: hành vi đó cần Play Mode/Profiler và phụ thuộc cách scene/instance bị hủy. F02 xác nhận điều chắc chắn hơn: `OnDispose` của Presenter và `ViewState` không được service gọi nếu không có shutdown path.

## 4. Async / UniTask

`InitializeAsync` tạo `_initializationCompletion` trước khi tạo State, signal trong `finally`; `DisposeAsync` đặt `_disposed`, tạo `_disposeCompletion`, rồi chạy `DisposeCoreAsync`. Cleanup bắt exception và chuyển vào completion source, nên `.Forget()` ở dòng `DisposeCoreAsync().Forget()` **không tự làm mất lỗi cleanup cho caller đang await `DisposeAsync()`**. Tuy nhiên `IDisposable.Dispose()` gọi `DisposeAsync().Forget()` nên caller không có completion/exception contract đồng bộ. [`Presenter.cs`](../Assets/MXEngine/MVP/Presenter.cs#L22).

Sample gọi `ExecuteAsync(...).Forget()` cho Unity button, nhưng method có `try/catch/finally` và log lỗi (L91–141), tốt hơn fire-and-forget không xử lý lỗi. Nó không có cancellation khi `OnDestroy`; `statusText.text` sau một await dài có thể chạy khi scene đang hủy (**Potential issue**). Service cũng không nhận token, nên request đã xếp hàng vẫn chạy trừ khi bên ngoài ngừng gọi.

**F01 deadlock cụ thể:** `ShowScreenAsync` giữ `_gate` (L40–102) trong lúc await `CreateAsync` → `presenter.InitializeAsync` (L304). Nếu `OnInitializeAsync` làm `await navigation.ShowModalAsync(...)`, lời gọi Modal chờ `_gate`; Screen chờ `OnInitializeAsync`; không có bên nào giải phóng. Cùng nguyên lý với `OnBindAsync` và `OnUnbindAsync` thông qua `ReleaseAsync`. `OnDispose` là hook đồng bộ, nên không trực tiếp `await` được. Đây là vấn đề lock scope, không phải lỗi UniTask riêng. `SemaphoreSlim` ngăn mutation đồng thời nhưng không hỗ trợ reentrancy.

**F06 exception:** `InitializeAsync` catch rồi `await DisposeAsync(); throw;`. Nếu unbind/`OnDispose`/`ViewState.OnDispose` ném, `throw` của lỗi init không chạy. `CreateAsync` còn có cleanup thứ hai và có thể thay thế lỗi một lần nữa. Cần giữ primary exception cho log/caller. Không thấy exception bị nuốt hoàn toàn ở đường `await` thông thường; vấn đề chính là bị thay thế hoặc không được caller await qua `IDisposable.Dispose()`.

## 5. NavigationService: tính nhất quán của state

| API / collection | Bình thường | Khi lỗi/cạnh biên |
| --- | --- | --- |
| `_screens` + `_screenReorderBuffer` | Top cùng ID trả Presenter cũ; ID nằm sâu được chuyển lên top; Screen mới ẩn top rồi push. Điều này tái sử dụng State/Presenter, không re-init. | `createPresenter`/`configurePresenter` mới **không chạy** khi ID đã mở; caller phải hiểu semantics. Tái dùng Screen đã bị destroy ngoài service có thể lỗi khi `SetActive`. |
| `BackToPreviousScreenAsync` | Chỉ pop nếu count ≥ 2; release top, bật Screen cũ trong `finally`. | Nếu release lỗi, method ném nhưng Screen trước vẫn được bật; pop đã commit. Đây là semantics chấp nhận được nếu được ghi rõ. Screen cuối không có API close. |
| `ShowModalAsync`, `_modals` | Mỗi call load/push instance mới, kể cả cùng ID; `CloseModalAsync` pop top. | Không có guard trùng ID; hợp lệ cho stack. `CloseAllModalsAsync` dừng ở lỗi đầu (F07). Modal cũ không tự inactive, phụ thuộc prefab chặn input. |
| `ShowOverlayAsync`, `_overlays` | Dictionary đảm bảo một instance mỗi ID; Hide remove rồi release. Overload không Presenter chỉ load component. | Không có thứ tự/priority overlay; `ShowOverlayAsync<TView>` không có catch quanh dictionary add, nhưng duplicate đã kiểm dưới gate nên lỗi ở add chỉ là trường hợp bất thường. Không coi đó là bug chính. |
| Replace Screen `stack:false` | Tạo Screen mới trước, pop/release toàn bộ cũ, push mới. | F03: release lỗi sau một hoặc nhiều pop khiến lịch sử đã thay đổi, Screen mới cũng bị release. Trạng thái cuối có thể còn ít Screen hơn trước request. |
| `_gate` | Tuần tự hóa request, kể cả load/init/release; ngăn hai thao tác cùng sửa collections. | F01, F04, F10. `IsInTransition` chỉ phản ánh gate đang giữ, không có event progress hoặc cancellation. |

`GetEntry` (`NavigationService.cs` L264–274) kiểm `Layer` và runtime key; điều này bắt lỗi cấu hình lúc mở. Nhưng `ViewCatalog.Get` chỉ chọn entry trùng ID đầu tiên: duplicate có thể tạo lỗi layer khó hiểu. Một catalog hợp lệ nên có đúng một entry cho mỗi ID và prefab root phải có component View yêu cầu.

## 6. Addressables, ownership, loader và testability

**Ownership hiện tại:** loader tạo instance; `NavigationService` giữ `OpenView` và gọi `ReleaseAsync` đúng một lần theo luồng đóng thông thường. Không có handle trả về từ `LoadAsync`; implementation dựa vào Addressables `trackHandle` của instance để release theo `GameObject`. Nếu component sai hoặc Addressables operation thất bại, catch release handle. Đây là đường hợp lý cho một loader Addressables cụ thể.

**Những giới hạn:** `ReleaseAsync(Component view)` không xác minh `Addressables.ReleaseInstance` trả `true` và không thể release nếu Unity đã xem component là null. `IViewLoader` không mang cancellation, không mang ownership token, và nhận trực tiếp `AssetReferenceGameObject`; mock được trong Unity Test Runner bằng fake Component/GameObject, nhưng không đủ trung lập cho loader Resources/pooling thuần C# nếu không đổi schema `ViewEntry`. Với pooling, thứ tự hiện tại `UnbindAsync` trước `ReleaseAsync` là điểm tốt; pool loader vẫn phải đảm bảo reset View/GameObject, và API cần phân biệt *return to pool* với *destroy* nếu behavior khác.

Một false positive cần tránh: comment XML của package Addressables nói `AssetReference.InstantiateAsync` không gọi lặp trước khi release, nhưng implementation Addressables 2.9.1 trong `Library/PackageCache/com.unity.addressables@8460f1c9c927/Runtime/AssetReference.cs` (L621–635) ở checkout này chuyển tiếp sang `Addressables.InstantiateAsync(RuntimeKey, ..., true)` mà không chặn operation lặp. Vì vậy **không** gắn lỗi “mở Modal cùng asset lần hai chắc chắn fail” cho source hiện tại; vẫn nên kiểm bằng Play Mode khi nâng package.

## 7. API design và SOLID theo source

`ShowScreenAsync<TPresenter,TView,TState>` dài nhưng generic constraint kiểm được Presenter đúng họ Screen và View đúng State ở compile time. Factory `Func<TView,TPresenter>` cho phép game tự truyền dependency mà không ép DI container. Đổi API chỉ để rút số generic argument sẽ đánh đổi type safety và độ rõ; chưa nên ưu tiên. Điểm dễ dùng sai là catalog/prefab chỉ được kiểm ở runtime, và `ShowScreenAsync` tái sử dụng theo ID nên factory/configure mới bị bỏ qua khi ID đã mở. Nên ghi semantics này vào API docs hoặc tách tên `ShowOrBringToFront` nếu behavior là chủ ý.

`new TState()` giảm boilerplate nhưng buộc State có constructor rỗng, khiến dữ liệu khởi tạo phải đưa qua Presenter/hook sau khi `View.BindAsync` đã bắt đầu. Với UI phức tạp, factory State hoặc init parameter rõ ràng có thể tốt hơn; đây là **Future concern**, không phải lỗi hiện tại. `ViewId` enum + catalog asset đơn giản, phù hợp số View nhỏ; nếu nhiều module phát triển song song, cơ chế registration/validation sẽ quan trọng hơn việc đổi enum ngay.

**SOLID:**

- **SRP:** `NavigationService` có nhiều việc liên quan chặt chẽ đến navigation, nên chưa nên tách chỉ vì số dòng. Nhưng animation, queue policy, cancellation và Addressables validation thêm vào đây sẽ làm nó khó kiểm chứng. `Presentr` giữ lifecycle nhất quán, hợp lý.
- **DIP:** service nhận `IViewLoader`, nhưng interface/catalog còn type Addressables (F09). Dependency inversion là một phần, không trọn vẹn.
- **OCP:** thêm View thường chỉ cần subclass + entry + enum; loader backend mới sẽ chạm nhiều nơi. Chỉ cải thiện khi có use case backend thứ hai.
- **LSP/ISP:** không tìm thấy vi phạm cụ thể. `ScreenPresenter`/`ModalPresenter` không override gì; `IViewLoader` chỉ có hai method liên quan cùng ownership. Không nên thêm interface chỉ để “đúng SOLID”.

## 8. Naming, readability và code quality

`Presentr` là typo công khai (F14). `Presenter.cs` chứa class tên khác file làm tìm kiếm khó hơn. `ViewState` dễ bị hiểu là domain Model trong khi thực tế chỉ là lifetime holder; cần đặt tên/README rõ ràng, chưa nhất thiết đổi type. `ViewEntry` dùng public mutable fields thuận Inspector nhưng không có validation. `NavigationService` tạo `OpenView` với `object Presenter` và `Func<UniTask> DisposePresenter`; đây là type erasure có chủ đích để lưu nhiều generic Presenter trong một collection, nhưng làm ownership khó thấy khi đọc nhanh. Comment về `DisposeAsync` và overlay không Presenter đang bổ ích; không có comment dư đáng kể. `ViewCatalog.Get` dùng vòng lặp không allocation lambda, phù hợp Unity.

## 9. Khi project lớn lên: current issue và future concern

| Tình huống production | Current issue đã có | Future scalability concern |
| --- | --- | --- |
| Hàng chục Screen | Screen cũ giữ prefab/State đến khi back/replace, là semantics hiện tại. | Cần giới hạn retention hoặc chính sách recreate cho màn nặng; `ViewId` enum/catalog lớn hơn cần validation/editor tooling. |
| Nhiều Modal/Overlay | Modal cùng ID được push nhiều lần; Overlay unique theo ID; không có shutdown và lỗi close-all là F02/F07. | Cần priority/sorting và focus/input policy nếu nhiều lớp chồng nhau. |
| Animation transition | `_gate` bao toàn bộ load/init/release và không có transition contract. | Thêm animation trong critical section có thể làm queue dài; cần định nghĩa khi nào View visible/interactable và lúc nào pop/commit. |
| Loading screen | Overlay yêu cầu giữa một request load bị gate chặn (F04). | Progress UI, cancel và timeout cần đường cập nhật độc lập với request đang giữ gate. |
| Network/async data | Hook async có thể giữ gate lâu; không có token (F01/F10). | Cần tách fetch dữ liệu khỏi lock navigation, xử lý timeout/cancel và stale result. |
| Pooling | `IViewLoader` chưa có token/lease; state cleanup phải hoàn tất trước return pool. | Reset trạng thái View/event/hierarchy và phân biệt instance đang sống/đang trả pool. |
| Localization | Base State/View không có notification. | Game cần chiến lược refresh UI khi locale đổi; hiện source chưa có localization subsystem. |

Đây là dự báo theo API hiện có, **không** khẳng định MXEngine đã có animation, networking, pooling hoặc localization.

## 10. Refactor plan

Mỗi phase chỉ nên bắt đầu sau khi có test tái hiện các điều kiện của phase trước. Đề xuất dưới đây không được thực hiện trong review này.

### Phase 1 — Fix correctness

| Vấn đề / vì sao | File | Cách sửa đề xuất | Lợi ích | Rủi ro khi sửa |
| --- | --- | --- | --- | --- |
| F01: hook await navigation dưới `_gate` deadlock. | `NavigationService.cs`, `Presenter.cs` | Chọn contract rõ: cấm reentrant navigation với guard/exception sớm **hoặc** chuyển hook dài ra ngoài critical section với state machine/queue. Viết test nested call trước. | Không treo UI; lỗi được báo rõ. | Di chuyển lock sai có thể tạo race/đúp View. |
| F02/F10: thiếu shutdown/cancel khiến owner không điều khiển được request/instance còn sống. | `NavigationService.cs`, `IViewLoader.cs`, sample `NavigationDemo.cs` | Thêm `ShutdownAsync` idempotent: ngừng nhận request, hủy/đợi in-flight, release mọi overlay/modal/screen; owner await khi kết thúc. Token đi qua loader/hook theo từng bước. | Lifetime rõ; scene transition an toàn hơn. | Thứ tự Unity `OnDestroy` và async teardown cần thiết kế/test kỹ. |
| F03/F07: bulk release partial khi hook/loader ném. | `NavigationService.cs` | Định nghĩa transaction/commit semantics; cleanup tất cả entry trong vòng lặp, lưu exception theo entry, khôi phục hoặc công bố trạng thái cuối nhất quán. | Không mất history âm thầm; close-all thực sự cố đóng hết. | Rollback View đã bị release có thể không khả thi; cần chọn semantics forward-only. |
| F06: lỗi teardown che lỗi khởi tạo. | `Presenter.cs`, `NavigationService.cs` | Giữ exception gốc, log/aggregate lỗi cleanup; đảm bảo View vẫn release. | Dễ chẩn đoán lỗi root cause. | Thay exception type/stack hiển thị cho caller. |
| F05/F13: trạng thái prefab/release chưa được quan sát đủ. | `AddressableViewLoader.cs`, `NavigationService.cs` | Test prefab active và View bị destroy ngoài service; chuẩn hóa inactive-before-bind, xác minh release result hoặc giữ handle. | Giảm flash/side effect và phát hiện release thất bại. | Thay thời điểm `Awake`/`OnEnable` có thể ảnh hưởng prefab hiện tại. |

### Phase 2 — Improve architecture

| Vấn đề / vì sao | File | Cách sửa đề xuất | Lợi ích | Rủi ro khi sửa |
| --- | --- | --- | --- | --- |
| F08: ownership Presenter mơ hồ. | `Presenter.cs`, `NavigationService.cs` | Ghi contract caller chỉ điều khiển Presenter, service sở hữu dispose; cân nhắc trả interface điều khiển không có `Dispose`, hoặc expose `Close` theo handle. | Tránh Presenter chết nhưng View vẫn active. | Thay public API; cần migration caller. |
| F09: loader abstraction lộ Addressables. | `IViewLoader.cs`, `ViewEntry.cs`, `ViewCatalog.cs`, `NavigationService.cs` | Chỉ khi có loader thứ hai: dùng descriptor/key trung lập + lease/instance token, để Addressable adapter chuyển đổi. | Fake/pool backend thay dễ hơn; ownership rõ. | Tăng số type và migration asset; không nên làm trước use case. |
| F11: catalog sai chỉ fail runtime. | `ViewCatalog.cs`, `ViewEntry.cs`, Editor tooling | Validate duplicate ID, invalid reference/layer, prefab root component; báo lỗi trong Inspector/build gate. | Bắt lỗi trước Play Mode/build. | Editor validation có thể chậm nếu load prefab mỗi lần sửa asset. |
| F12: State disposal không terminal khi hook ném. | `ViewState.cs` | Đặt cờ disposed trước `OnDispose`; test hook throw/repeat call. | Idempotency rõ. | Thay semantics retry cleanup nếu game từng dựa vào nó. |

### Phase 3 — Improve extensibility

| Vấn đề / vì sao | File | Cách sửa đề xuất | Lợi ích | Rủi ro khi sửa |
| --- | --- | --- | --- | --- |
| Transition/network async dễ kéo dài gate. | `NavigationService.cs`, Presenter/View hooks | Định nghĩa transition state (loading, enter, active, exit), chính sách queue, progress/cancel; không ép overlay dùng chung gate dài. | Mở rộng animation/loading đáng tin cậy. | State machine phức tạp; chỉ làm khi có use case thực. |
| State không có cập nhật tự động. | `ViewState.cs`, `View.cs` | Chọn một contract nhỏ: Presenter gọi render tường minh hoặc State phát change event; test unbind event. | Dễ cập nhật UI async/localization. | Reactive system lớn có thể tăng allocation và coupling. |
| Pooling cần reset và ownership. | `IViewLoader.cs`, implementation mới | Sau khi teardown deterministic, thêm pool loader trả cùng lease; chuẩn hóa `OnUnbind`/reset transient state. | Giảm instantiate cost cho màn mở thường xuyên. | Reuse có thể giữ listener/state cũ nếu reset thiếu. |

### Phase 4 — Optional improvements

| Vấn đề / vì sao | File | Cách sửa đề xuất | Lợi ích | Rủi ro khi sửa |
| --- | --- | --- | --- | --- |
| F14 typo `Presentr`. | `Presenter.cs`, mọi caller | Đổi thành `Presenter` trong một đợt migration có compatibility alias/timeline rõ. | API dễ đọc và tìm kiếm. | Breaking change cho code game. |
| Generic call dài và enum tập trung. | `NavigationService.cs`, `ViewId.cs` | Chỉ thêm typed route/helper khi số call site thực sự gây lỗi hoặc nhiều module cần registration. | Call site ngắn hơn, module tách hơn. | Mất compile-time clarity, thêm abstraction không cần thiết. |
| Thiếu sample event/data flow và test suite. | `Samples/NavigationDemo`, test assembly mới | Thêm một View có button event + State render; test init/dispose/failure/reentrancy/replace. | Onboarding và regression tốt hơn. | Cần bảo trì sample/test khi API đổi. |

## Refactor Priority

| Priority | Việc nên làm trước | Điều kiện hoàn thành |
| --- | --- | --- |
| **P0 — correctness / crash / leak** | F01, F02, F03, F07; kiểm F05/F13 bằng Play Mode. | Test nested navigation không treo; shutdown gọi đủ Presenter/State teardown; replace/close-all có state nhất quán khi release ném; Addressables instance được release đúng. |
| **P1 — architecture problem đáng sửa sớm** | F04, F06, F08, F10. | Loading overlay không bị chặn bởi load dài; lỗi gốc còn thấy; ownership/cancel contract rõ. |
| **P2 — maintainability** | F09, F11, F12 và sample/test event flow. | Catalog validation báo lỗi trước runtime; State dispose idempotent kể cả hook lỗi; loader contract phản ánh backend cần hỗ trợ. |
| **P3 — optional** | F14, typed helper/route và tối ưu khác theo profile. | Có dữ liệu call site/profiler hoặc kế hoạch migration trước khi đổi API. |

**Giới hạn bằng chứng:** các nhánh deadlock/partial commit được suy ra trực tiếp từ control flow; hiệu ứng flash, thời điểm Unity destroy và Addressables release trên scene unload cần xác minh trong Play Mode/Profiler. Review này không thay thế test trên player build.

