using System.Collections.Generic;
using UnityEngine;

// 인덱스 기반으로 자라나는 컴포넌트 풀 - 필요한 만큼 Instantiate하고,
// 이번 갱신에서 쓰지 않은 뒷부분은 비활성화한다.
// MouseSelectController(하이라이트 스프라이트), ChunkDebugger(청크 색상 오버레이),
// ConqueredChunkBorderRenderer(점령 테두리 LineRenderer)가 각자 구현하던 풀링 로직을 통합했다.
public class ComponentPool<T> where T : Component
{
    private readonly T _prefab;
    private readonly Transform _parent;
    private readonly List<T> _pool = new();

    // seedInstance를 넘기면 새로 Instantiate하지 않고 기존 인스턴스를 0번 슬롯으로 사용한다
    // (예: 인스펙터에 미리 배치해 둔 렌더러를 그대로 재사용하고 싶을 때).
    public ComponentPool(T prefab, Transform parent, T seedInstance = null)
    {
        _prefab = prefab;
        _parent = parent;

        if (seedInstance != null)
            _pool.Add(seedInstance);
    }

    public T Get(int index)
    {
        if (index >= _pool.Count)
            _pool.Add(Object.Instantiate(_prefab, _parent));

        T item = _pool[index];
        item.gameObject.SetActive(true);
        return item;
    }

    // index부터 끝까지 비활성화 - 이번 갱신에서 쓰지 않은 나머지 풀 항목을 숨긴다.
    public void DeactivateFrom(int usedCount)
    {
        for (int i = usedCount; i < _pool.Count; i++)
            _pool[i].gameObject.SetActive(false);
    }

    public void DeactivateAll() => DeactivateFrom(0);
}
