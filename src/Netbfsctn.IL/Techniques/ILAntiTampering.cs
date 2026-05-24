using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Netbfsctn.Core.Pipeline;
using Netbfsctn.Core.Techniques;

namespace Netbfsctn.IL.Techniques;

/// <summary>
/// 改ざん検出 (Anti-Tampering)。
///
/// 全型・全メソッドの (FullName, parameter count) を集約した 32bit hash を計算し、
/// 暗号化リソースに格納する。モジュール初期化子で同じ hash を実行時に再計算して比較し、
/// 不一致なら <see cref="Environment.FailFast(string?)"/> を呼ぶ。
///
/// IL 本体の bytes 全体を hash しないのは、パイプライン後段の
/// <c>SimplifyBranches</c> / <c>OptimizeBranches</c> や JIT 最適化で
/// bytes 長が変わって偽陽性を起こすため。構造 (型名・メソッド名・arity) ベースなので
/// 安定して検出できる範囲は限定的だが、メソッドの追加・削除・リネームは捕捉できる。
/// </summary>
public class ILAntiTampering : IObfuscationTechnique<ModuleDef>
{
    public string Name => "Anti-Tampering (IL)";

    private const string HashResourceName = "​‍‌";
    private const string CheckMethodName = "​‍‌​";

    public void Apply(ModuleDef module, ObfuscationContext context, ObfuscationResult result)
    {
        var importer = new Importer(module);

        // チェックメソッドを先に作成 (ハッシュ計算から除外するため自身を識別できる必要あり)
        var checkMethod = CreateCheckMethod(module, importer);

        // 構造ハッシュ (型名 + メソッド名 + arity) を計算
        var hash = ComputeStructuralHash(module, checkMethod);

        // ハッシュ値を EmbeddedResource として格納
        var hashBytes = BitConverter.GetBytes(hash);
        module.Resources.Add(new EmbeddedResource(HashResourceName, hashBytes, ManifestResourceAttributes.Private));

        // モジュール初期化子から checkMethod を呼ぶ
        InjectCheckInModuleCctor(module, checkMethod);

        result.AntiTamperingApplied = true;
        context.Logger.Verbose($"改ざん検出を注入しました (ハッシュ: 0x{hash:X8})");
    }

    /// <summary>
    /// 全型・全メソッドを反復し、(FullName, params count) を hash に混ぜる。
    /// checkMethod 自身、<c>&lt;Module&gt;</c>、<c>&lt;PrivateImplementationDetails&gt;</c> 型は除外する。
    /// 実行時 ( <see cref="BuildCheckBody"/> の生成する IL) と一致する正規化が必要:
    ///   - nested 区切りは <c>+</c> (Reflection の FullName 仕様に合わせる)
    ///   - parameter count は <c>this</c> を除外 (Reflection.GetParameters() に合わせる)
    /// </summary>
    private static uint ComputeStructuralHash(ModuleDef module, MethodDef excludeMethod)
    {
        uint hash = 0;
        foreach (var type in module.GetTypes())
        {
            if (type.Name == "<Module>") continue;

            var normalizedFullName = type.FullName?.Replace('/', '+');
            // <PrivateImplementationDetails> 自身と、その nested type もすべてスキップする
            if (normalizedFullName != null
                && normalizedFullName.StartsWith("<PrivateImplementationDetails>", StringComparison.Ordinal))
                continue;

            hash = HashString(hash, normalizedFullName);

            foreach (var method in type.Methods)
            {
                if (method == excludeMethod) continue;
                if (method.IsConstructor) continue;
                hash = HashString(hash, method.Name);
                // MethodSig.Params は this を含まない (Reflection の GetParameters と同じカウント)
                var paramCount = method.MethodSig?.Params?.Count ?? 0;
                hash ^= (uint)(paramCount & 0xFFFF);
                hash = RotateLeft(hash, 5);
            }
        }
        return hash;
    }

    private static uint HashString(uint h, string? s)
    {
        if (s == null) return h;
        foreach (var c in s)
        {
            h ^= c;
            h = RotateLeft(h, 7);
        }
        return h;
    }

    private static uint RotateLeft(uint v, int n) => (v << n) | (v >> (32 - n));

    private static MethodDef CreateCheckMethod(ModuleDef module, Importer importer)
    {
        var moduleType = module.GlobalType;

        var checkMethod = new MethodDefUser(
            CheckMethodName,
            MethodSig.CreateStatic(module.CorLibTypes.Void),
            MethodImplAttributes.IL | MethodImplAttributes.Managed,
            MethodAttributes.Static | MethodAttributes.Private | MethodAttributes.HideBySig);

        BuildCheckBody(checkMethod, module, importer);
        moduleType.Methods.Add(checkMethod);
        return checkMethod;
    }

    /// <summary>
    /// IL 疑似コード:
    /// <code>
    /// void Check() {
    ///   var asm = Assembly.GetExecutingAssembly();
    ///   var types = asm.GetTypes();
    ///   uint computed = 0;
    ///   for (int ti = 0; ti &lt; types.Length; ti++) {
    ///     var type = types[ti];
    ///     var typeName = type.FullName;
    ///     if (typeName == "&lt;Module&gt;") continue;
    ///     computed = HashString(computed, typeName);
    ///     var methods = type.GetMethods(All);
    ///     for (int mi = 0; mi &lt; methods.Length; mi++) {
    ///       var m = methods[mi];
    ///       if (m.Name == "&lt;CheckMethodName&gt;" &amp;&amp; m.DeclaringType == moduleGlobalType) continue;
    ///       computed = HashString(computed, m.Name);
    ///       computed ^= (uint)(m.GetParameters().Length &amp; 0xFFFF);
    ///       computed = RotateLeft(computed, 5);
    ///     }
    ///   }
    ///   var stream = asm.GetManifestResourceStream("&lt;HashResourceName&gt;");
    ///   if (stream == null) return;
    ///   var buf = new byte[4];
    ///   stream.Read(buf, 0, 4);
    ///   var stored = BitConverter.ToUInt32(buf, 0);
    ///   if (computed != stored) Environment.FailFast(null);
    /// }
    /// </code>
    ///
    /// 簡略化のため "GetMethods" は 1 つの BindingFlags 引数オーバーロードを使用し、
    /// DeclaredOnly | Public | NonPublic | Static | Instance を渡す。
    /// </summary>
    private static void BuildCheckBody(MethodDefUser method, ModuleDef module, Importer importer)
    {
        var body = new CilBody();
        method.Body = body;
        body.InitLocals = true;

        var asmSig = importer.ImportAsTypeSig(typeof(System.Reflection.Assembly));
        var typeSig = importer.ImportAsTypeSig(typeof(Type));
        var typeArrSig = new SZArraySig(typeSig);
        var miSig = importer.ImportAsTypeSig(typeof(System.Reflection.MethodInfo));
        var miArrSig = new SZArraySig(miSig);
        var streamSig = importer.ImportAsTypeSig(typeof(Stream));
        var byteArrSig = new SZArraySig(module.CorLibTypes.Byte);

        var localAsm = new Local(asmSig);                       // 0
        var localTypes = new Local(typeArrSig);                 // 1
        var localTi = new Local(module.CorLibTypes.Int32);      // 2
        var localType = new Local(typeSig);                     // 3
        var localTypeName = new Local(module.CorLibTypes.String); // 4
        var localMethods = new Local(miArrSig);                 // 5
        var localMi = new Local(module.CorLibTypes.Int32);      // 6
        var localMethod = new Local(miSig);                     // 7
        var localMethodName = new Local(module.CorLibTypes.String); // 8
        var localStrIdx = new Local(module.CorLibTypes.Int32);  // 9
        var localHash = new Local(module.CorLibTypes.UInt32);   // 10
        var localStream = new Local(streamSig);                 // 11
        var localBuf = new Local(byteArrSig);                   // 12
        var localStored = new Local(module.CorLibTypes.UInt32); // 13

        body.Variables.Add(localAsm);
        body.Variables.Add(localTypes);
        body.Variables.Add(localTi);
        body.Variables.Add(localType);
        body.Variables.Add(localTypeName);
        body.Variables.Add(localMethods);
        body.Variables.Add(localMi);
        body.Variables.Add(localMethod);
        body.Variables.Add(localMethodName);
        body.Variables.Add(localStrIdx);
        body.Variables.Add(localHash);
        body.Variables.Add(localStream);
        body.Variables.Add(localBuf);
        body.Variables.Add(localStored);

        var getExecAsm = importer.Import(typeof(System.Reflection.Assembly).GetMethod("GetExecutingAssembly")!);
        var getTypes = importer.Import(typeof(System.Reflection.Assembly).GetMethod("GetTypes")!);
        var typeFullName = importer.Import(typeof(Type).GetProperty("FullName")!.GetMethod!);
        var getMethods = importer.Import(typeof(Type).GetMethod("GetMethods",
            [typeof(System.Reflection.BindingFlags)])!);
        var miName = importer.Import(typeof(System.Reflection.MemberInfo).GetProperty("Name")!.GetMethod!);
        var miGetParams = importer.Import(typeof(System.Reflection.MethodBase).GetMethod("GetParameters")!);
        var stringEquals = importer.Import(typeof(string).GetMethod("op_Equality",
            [typeof(string), typeof(string)])!);
        var stringLength = importer.Import(typeof(string).GetProperty("Length")!.GetMethod!);
        var stringChars = importer.Import(typeof(string).GetMethod("get_Chars",
            [typeof(int)])!);
        var stringStartsWith = importer.Import(typeof(string).GetMethod("StartsWith",
            [typeof(string)])!);
        var getManifestStream = importer.Import(typeof(System.Reflection.Assembly).GetMethod(
            "GetManifestResourceStream", [typeof(string)])!);
        var streamRead = importer.Import(typeof(Stream).GetMethod("Read",
            [typeof(byte[]), typeof(int), typeof(int)])!);
        var toUInt32 = importer.Import(typeof(BitConverter).GetMethod("ToUInt32",
            [typeof(byte[]), typeof(int)])!);
        var failFast = importer.Import(typeof(Environment).GetMethod("FailFast",
            [typeof(string)])!);

        const int bindingFlagsAll =
            (int)(System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Static
                | System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.DeclaredOnly);

        // computed = 0
        body.Instructions.Add(OpCodes.Ldc_I4_0.ToInstruction());
        body.Instructions.Add(OpCodes.Stloc.ToInstruction(localHash));

        // asm = Assembly.GetExecutingAssembly()
        body.Instructions.Add(OpCodes.Call.ToInstruction(getExecAsm));
        body.Instructions.Add(OpCodes.Stloc.ToInstruction(localAsm));

        // types = asm.GetTypes()
        body.Instructions.Add(OpCodes.Ldloc.ToInstruction(localAsm));
        body.Instructions.Add(OpCodes.Callvirt.ToInstruction(getTypes));
        body.Instructions.Add(OpCodes.Stloc.ToInstruction(localTypes));

        // ti = 0
        body.Instructions.Add(OpCodes.Ldc_I4_0.ToInstruction());
        body.Instructions.Add(OpCodes.Stloc.ToInstruction(localTi));

        var typeLoopCondition = new Instruction(OpCodes.Ldloc, localTi);
        var typeLoopBody = new Instruction(OpCodes.Ldloc, localTypes);
        var typeContinue = new Instruction(OpCodes.Ldloc, localTi);

        body.Instructions.Add(OpCodes.Br.ToInstruction(typeLoopCondition));

        // === type loop body ===
        body.Instructions.Add(typeLoopBody);                                 // ldloc types
        body.Instructions.Add(OpCodes.Ldloc.ToInstruction(localTi));
        body.Instructions.Add(OpCodes.Ldelem_Ref.ToInstruction());
        body.Instructions.Add(OpCodes.Stloc.ToInstruction(localType));

        // typeName = type.FullName
        body.Instructions.Add(OpCodes.Ldloc.ToInstruction(localType));
        body.Instructions.Add(OpCodes.Callvirt.ToInstruction(typeFullName));
        body.Instructions.Add(OpCodes.Stloc.ToInstruction(localTypeName));

        // if (typeName == null) skip
        body.Instructions.Add(OpCodes.Ldloc.ToInstruction(localTypeName));
        body.Instructions.Add(OpCodes.Brfalse.ToInstruction(typeContinue));

        // if (typeName == "<Module>") skip
        body.Instructions.Add(OpCodes.Ldloc.ToInstruction(localTypeName));
        body.Instructions.Add(OpCodes.Ldstr.ToInstruction("<Module>"));
        body.Instructions.Add(OpCodes.Call.ToInstruction(stringEquals));
        body.Instructions.Add(OpCodes.Brtrue.ToInstruction(typeContinue));

        // if (typeName.StartsWith("<PrivateImplementationDetails>")) skip
        body.Instructions.Add(OpCodes.Ldloc.ToInstruction(localTypeName));
        body.Instructions.Add(OpCodes.Ldstr.ToInstruction("<PrivateImplementationDetails>"));
        body.Instructions.Add(OpCodes.Callvirt.ToInstruction(stringStartsWith));
        body.Instructions.Add(OpCodes.Brtrue.ToInstruction(typeContinue));

        // hash = HashString(hash, typeName)
        EmitHashString(body, localHash, localTypeName, localStrIdx, stringLength, stringChars);

        // methods = type.GetMethods(bindingFlagsAll)
        body.Instructions.Add(OpCodes.Ldloc.ToInstruction(localType));
        body.Instructions.Add(Instruction.CreateLdcI4(bindingFlagsAll));
        body.Instructions.Add(OpCodes.Callvirt.ToInstruction(getMethods));
        body.Instructions.Add(OpCodes.Stloc.ToInstruction(localMethods));

        // mi = 0
        body.Instructions.Add(OpCodes.Ldc_I4_0.ToInstruction());
        body.Instructions.Add(OpCodes.Stloc.ToInstruction(localMi));

        var methodLoopCondition = new Instruction(OpCodes.Ldloc, localMi);
        var methodLoopBody = new Instruction(OpCodes.Ldloc, localMethods);
        var methodContinue = new Instruction(OpCodes.Ldloc, localMi);

        body.Instructions.Add(OpCodes.Br.ToInstruction(methodLoopCondition));

        // === method loop body ===
        body.Instructions.Add(methodLoopBody);                                // ldloc methods
        body.Instructions.Add(OpCodes.Ldloc.ToInstruction(localMi));
        body.Instructions.Add(OpCodes.Ldelem_Ref.ToInstruction());
        body.Instructions.Add(OpCodes.Stloc.ToInstruction(localMethod));

        // methodName = method.Name
        body.Instructions.Add(OpCodes.Ldloc.ToInstruction(localMethod));
        body.Instructions.Add(OpCodes.Callvirt.ToInstruction(miName));
        body.Instructions.Add(OpCodes.Stloc.ToInstruction(localMethodName));

        // if (methodName == CheckMethodName) skip
        body.Instructions.Add(OpCodes.Ldloc.ToInstruction(localMethodName));
        body.Instructions.Add(OpCodes.Ldstr.ToInstruction(CheckMethodName));
        body.Instructions.Add(OpCodes.Call.ToInstruction(stringEquals));
        body.Instructions.Add(OpCodes.Brtrue.ToInstruction(methodContinue));

        // hash = HashString(hash, methodName)
        EmitHashString(body, localHash, localMethodName, localStrIdx, stringLength, stringChars);

        // hash ^= (uint)(method.GetParameters().Length & 0xFFFF)
        body.Instructions.Add(OpCodes.Ldloc.ToInstruction(localHash));
        body.Instructions.Add(OpCodes.Ldloc.ToInstruction(localMethod));
        body.Instructions.Add(OpCodes.Callvirt.ToInstruction(miGetParams));
        body.Instructions.Add(OpCodes.Ldlen.ToInstruction());
        body.Instructions.Add(OpCodes.Conv_I4.ToInstruction());
        body.Instructions.Add(Instruction.CreateLdcI4(0xFFFF));
        body.Instructions.Add(OpCodes.And.ToInstruction());
        body.Instructions.Add(OpCodes.Xor.ToInstruction());
        // hash = RotateLeft(hash, 5)  ==  (hash << 5) | (hash >> 27)
        body.Instructions.Add(OpCodes.Dup.ToInstruction());
        body.Instructions.Add(OpCodes.Ldc_I4_5.ToInstruction());
        body.Instructions.Add(OpCodes.Shl.ToInstruction());
        // stack: [..., hash, hash<<5]
        // need: hash<<5 | hash>>27
        // Re-load hash for the right shift
        // Actually current stack is [..., hash, hash<<5] after Dup; Shl pops only the original hash count.
        // Let me redo: we have hash on top; Dup makes [..., hash, hash]; ldc.i4.5 -> [..., hash, hash, 5]; Shl -> [..., hash, hash<<5]
        // Then we want hash>>27 and OR. So:
        // swap stack? IL has no swap. Use temp via stloc.
        // Simpler: compute differently.
        body.Instructions.Add(OpCodes.Stloc.ToInstruction(localHash)); // temporarily store hash<<5
        body.Instructions.Add(OpCodes.Ldc_I4_S.ToInstruction((sbyte)27));
        body.Instructions.Add(OpCodes.Shr_Un.ToInstruction());          // hash>>27 (uses the duplicated hash)
        body.Instructions.Add(OpCodes.Ldloc.ToInstruction(localHash));  // load back hash<<5
        body.Instructions.Add(OpCodes.Or.ToInstruction());              // (hash>>27) | (hash<<5)
        body.Instructions.Add(OpCodes.Stloc.ToInstruction(localHash));

        // methodContinue: mi++
        body.Instructions.Add(methodContinue);                            // ldloc mi
        body.Instructions.Add(OpCodes.Ldc_I4_1.ToInstruction());
        body.Instructions.Add(OpCodes.Add.ToInstruction());
        body.Instructions.Add(OpCodes.Stloc.ToInstruction(localMi));

        // method loop condition: mi < methods.Length
        body.Instructions.Add(methodLoopCondition);                       // ldloc mi
        body.Instructions.Add(OpCodes.Ldloc.ToInstruction(localMethods));
        body.Instructions.Add(OpCodes.Ldlen.ToInstruction());
        body.Instructions.Add(OpCodes.Conv_I4.ToInstruction());
        body.Instructions.Add(OpCodes.Blt.ToInstruction(methodLoopBody));

        // typeContinue: ti++
        body.Instructions.Add(typeContinue);                              // ldloc ti
        body.Instructions.Add(OpCodes.Ldc_I4_1.ToInstruction());
        body.Instructions.Add(OpCodes.Add.ToInstruction());
        body.Instructions.Add(OpCodes.Stloc.ToInstruction(localTi));

        // type loop condition: ti < types.Length
        body.Instructions.Add(typeLoopCondition);                         // ldloc ti
        body.Instructions.Add(OpCodes.Ldloc.ToInstruction(localTypes));
        body.Instructions.Add(OpCodes.Ldlen.ToInstruction());
        body.Instructions.Add(OpCodes.Conv_I4.ToInstruction());
        body.Instructions.Add(OpCodes.Blt.ToInstruction(typeLoopBody));

        // === post-loop: compare with stored hash ===

        // stream = asm.GetManifestResourceStream(HashResourceName)
        body.Instructions.Add(OpCodes.Ldloc.ToInstruction(localAsm));
        body.Instructions.Add(OpCodes.Ldstr.ToInstruction(HashResourceName));
        body.Instructions.Add(OpCodes.Callvirt.ToInstruction(getManifestStream));
        body.Instructions.Add(OpCodes.Stloc.ToInstruction(localStream));

        // if (stream == null) return
        var retInstr = new Instruction(OpCodes.Ret);
        body.Instructions.Add(OpCodes.Ldloc.ToInstruction(localStream));
        body.Instructions.Add(OpCodes.Brfalse.ToInstruction(retInstr));

        // buf = new byte[4]; stream.Read(buf, 0, 4)
        body.Instructions.Add(OpCodes.Ldc_I4_4.ToInstruction());
        body.Instructions.Add(OpCodes.Newarr.ToInstruction(module.CorLibTypes.Byte.TypeDefOrRef));
        body.Instructions.Add(OpCodes.Stloc.ToInstruction(localBuf));
        body.Instructions.Add(OpCodes.Ldloc.ToInstruction(localStream));
        body.Instructions.Add(OpCodes.Ldloc.ToInstruction(localBuf));
        body.Instructions.Add(OpCodes.Ldc_I4_0.ToInstruction());
        body.Instructions.Add(OpCodes.Ldc_I4_4.ToInstruction());
        body.Instructions.Add(OpCodes.Callvirt.ToInstruction(streamRead));
        body.Instructions.Add(OpCodes.Pop.ToInstruction());

        // stored = BitConverter.ToUInt32(buf, 0)
        body.Instructions.Add(OpCodes.Ldloc.ToInstruction(localBuf));
        body.Instructions.Add(OpCodes.Ldc_I4_0.ToInstruction());
        body.Instructions.Add(OpCodes.Call.ToInstruction(toUInt32));
        body.Instructions.Add(OpCodes.Stloc.ToInstruction(localStored));

        // if (computed == stored) return
        body.Instructions.Add(OpCodes.Ldloc.ToInstruction(localHash));
        body.Instructions.Add(OpCodes.Ldloc.ToInstruction(localStored));
        body.Instructions.Add(OpCodes.Beq.ToInstruction(retInstr));

        // Environment.FailFast(null)
        body.Instructions.Add(OpCodes.Ldnull.ToInstruction());
        body.Instructions.Add(OpCodes.Call.ToInstruction(failFast));

        body.Instructions.Add(retInstr);
        body.KeepOldMaxStack = true;
    }

    /// <summary>
    /// hash = HashString(hash, str) を生成。
    /// for (int i = 0; i &lt; str.Length; i++) { hash ^= str[i]; hash = RotateLeft(hash, 7); }
    /// </summary>
    private static void EmitHashString(
        CilBody body, Local localHash, Local localStr, Local localIdx,
        IMethod stringLength, IMethod stringChars)
    {
        // idx = 0
        body.Instructions.Add(OpCodes.Ldc_I4_0.ToInstruction());
        body.Instructions.Add(OpCodes.Stloc.ToInstruction(localIdx));

        var condition = new Instruction(OpCodes.Ldloc, localIdx);
        var loopBody = new Instruction(OpCodes.Ldloc, localHash);
        body.Instructions.Add(OpCodes.Br.ToInstruction(condition));

        body.Instructions.Add(loopBody);                                  // ldloc hash
        body.Instructions.Add(OpCodes.Ldloc.ToInstruction(localStr));
        body.Instructions.Add(OpCodes.Ldloc.ToInstruction(localIdx));
        body.Instructions.Add(OpCodes.Callvirt.ToInstruction(stringChars));
        body.Instructions.Add(OpCodes.Xor.ToInstruction());
        // RotateLeft(h, 7) == (h << 7) | (h >> 25)
        body.Instructions.Add(OpCodes.Dup.ToInstruction());
        body.Instructions.Add(OpCodes.Ldc_I4_7.ToInstruction());
        body.Instructions.Add(OpCodes.Shl.ToInstruction());
        body.Instructions.Add(OpCodes.Stloc.ToInstruction(localHash));    // temp hash<<7
        body.Instructions.Add(Instruction.CreateLdcI4(25));
        body.Instructions.Add(OpCodes.Shr_Un.ToInstruction());            // h >> 25
        body.Instructions.Add(OpCodes.Ldloc.ToInstruction(localHash));    // load hash<<7
        body.Instructions.Add(OpCodes.Or.ToInstruction());
        body.Instructions.Add(OpCodes.Stloc.ToInstruction(localHash));

        // idx++
        body.Instructions.Add(OpCodes.Ldloc.ToInstruction(localIdx));
        body.Instructions.Add(OpCodes.Ldc_I4_1.ToInstruction());
        body.Instructions.Add(OpCodes.Add.ToInstruction());
        body.Instructions.Add(OpCodes.Stloc.ToInstruction(localIdx));

        // i < str.Length
        body.Instructions.Add(condition);                                 // ldloc idx
        body.Instructions.Add(OpCodes.Ldloc.ToInstruction(localStr));
        body.Instructions.Add(OpCodes.Callvirt.ToInstruction(stringLength));
        body.Instructions.Add(OpCodes.Blt.ToInstruction(loopBody));
    }

    private static void InjectCheckInModuleCctor(ModuleDef module, MethodDef checkMethod)
    {
        var moduleType = module.GlobalType;
        var cctor = moduleType.FindStaticConstructor();

        if (cctor == null)
        {
            cctor = new MethodDefUser(
                ".cctor",
                MethodSig.CreateStatic(module.CorLibTypes.Void),
                MethodImplAttributes.IL | MethodImplAttributes.Managed,
                MethodAttributes.Static | MethodAttributes.Private | MethodAttributes.HideBySig
                    | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName);
            cctor.Body = new CilBody();
            cctor.Body.Instructions.Add(new Instruction(OpCodes.Ret));
            moduleType.Methods.Add(cctor);
        }

        cctor.Body.Instructions.Insert(0, new Instruction(OpCodes.Call, checkMethod));
    }
}
