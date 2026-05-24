namespace Netbfsctn.IL.VM;

/// <summary>
/// VM の命令セットアーキテクチャ (Instruction Set Architecture)。
/// 難読化セッション毎に opcode → 1 つ以上のバイト値の写像をランダムに割り当てる。
/// ホット opcode はポリモーフィック (複数バイトで同一意味) にすることで、
/// KoiVM 系の固定 opcode 前提 devirtualizer を無効化する。
/// </summary>
public class VmIsa
{
    /// <summary>
    /// ポリモーフィック化対象 opcode。これらは難読化セッション毎に複数の異なる
    /// バイト値が割り当てられ、変換時にランダムに選択される。
    /// </summary>
    private static readonly VmOpCode[] PolymorphicOps =
    [
        VmOpCode.ADD,
        VmOpCode.SUB,
        VmOpCode.LDC_I4,
        VmOpCode.LDLOC,
        VmOpCode.LDARG
    ];

    private const int PolymorphicVariantCount = 2;

    private readonly Dictionary<VmOpCode, byte[]> _encodings = new();
    private readonly Random _pickRng;

    public IReadOnlyDictionary<VmOpCode, byte[]> Encodings => _encodings;

    public VmIsa(int seed)
    {
        var assignRng = new Random(seed);
        _pickRng = new Random(unchecked(seed ^ 0x1C0DE));

        // 0x00 は予約 (汎用 "fallthrough" として残す)
        var pool = new List<byte>();
        for (var i = 1; i <= 255; i++) pool.Add((byte)i);
        Shuffle(pool, assignRng);

        var idx = 0;
        foreach (var op in Enum.GetValues<VmOpCode>())
        {
            var count = PolymorphicOps.Contains(op) ? PolymorphicVariantCount : 1;
            if (idx + count > pool.Count)
                throw new InvalidOperationException("VmIsa: opcode pool exhausted");

            var bytes = new byte[count];
            for (var i = 0; i < count; i++) bytes[i] = pool[idx++];
            _encodings[op] = bytes;
        }
    }

    /// <summary>
    /// 指定 opcode のエンコーディングを 1 つランダムに選ぶ (翻訳時に使用)。
    /// ポリモーフィックな opcode では呼び出し毎に異なるバイトが返る。
    /// </summary>
    public byte PickEncoding(VmOpCode op)
    {
        var bytes = _encodings[op];
        return bytes[_pickRng.Next(bytes.Length)];
    }

    /// <summary>
    /// 指定 opcode の全エンコーディングを返す (インタープリタ dispatch 生成時に使用)。
    /// </summary>
    public IReadOnlyList<byte> GetAllEncodings(VmOpCode op) => _encodings[op];

    private static void Shuffle<T>(List<T> list, Random rng)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
