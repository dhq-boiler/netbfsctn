using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Netbfsctn.Core.Pipeline;
using Netbfsctn.Core.Techniques;

namespace Netbfsctn.IL.Techniques;

/// <summary>
/// NecroBit: 定数だけを返すメソッドを検出し、ボディを
/// <c>R(id, args)</c> へのインダイレクト呼び出しに置き換える。
/// 元々の戻り値 (ボックス化) は難読化されたリソースから復元される。
///
/// 対象は副作用なしで静的に戻り値を評価できるメソッドのみ。
/// (ダミーメソッド + 引数/ローカル/フィールド参照のない private 静的メソッド)
/// </summary>
public class ILNecroBit : IObfuscationTechnique<ModuleDef>
{
    public string Name => "NecroBit (IL)";

    private const string ResourceName = "\u200C\u200B\u200D";
    private const byte XorKey = 0xC7;

    // type tags for serialized return values
    private const byte TagNull = 0;
    private const byte TagInt32 = 1;
    private const byte TagInt64 = 2;
    private const byte TagBool = 3;
    private const byte TagSingle = 4;
    private const byte TagDouble = 5;

    public void Apply(ModuleDef module, ObfuscationContext context, ObfuscationResult result)
    {
        var importer = new Importer(module);

        // ヘルパー型を注入
        var helperType = InjectNecroBitHelper(module, importer);
        var restoreMethod = helperType.Methods.First(m => m.Name == "R");

        var entries = new List<(int id, byte tag, byte[] payload)>();
        var methodId = 0;

        foreach (var type in module.Types.ToList())
        {
            if (type.Name == "<Module>" || type == helperType)
                continue;

            foreach (var method in type.Methods.ToList())
            {
                if (!IsEligible(method))
                    continue;

                if (!TryEvaluateConstantReturn(method, out var tag, out var payload))
                    continue;

                entries.Add((methodId, tag, payload));

                ReplaceBodyWithStub(method, module, restoreMethod, methodId);

                methodId++;
                result.EncryptedMethodBodies++;
                context.Logger.Verbose($"NecroBit: {method.Name}");
            }
        }

        if (entries.Count > 0)
        {
            var resourceData = SerializeEntries(entries);
            module.Resources.Add(new EmbeddedResource(ResourceName, resourceData, ManifestResourceAttributes.Private));
        }
    }

    private static bool IsEligible(MethodDef method)
    {
        if (!method.HasBody) return false;
        if (method.IsPublic) return false;
        if (method.IsVirtual) return false;
        if (method.IsConstructor) return false;
        if (method.HasGenericParameters) return false;
        if (method.Body.ExceptionHandlers.Count > 0) return false;
        if (method.Body.Instructions.Count < 2) return false;
        if (method.Name == "Main") return false;
        // 引数のあるメソッドは静的評価できないのでスキップ
        if (method.Parameters.Count > 0) return false;
        // インスタンスメソッドもスキップ (this 引数あり)
        if (!method.IsStatic) return false;
        return true;
    }

    /// <summary>
    /// 副作用のない純粋な定数計算メソッドを抽象実行して戻り値を得る。
    /// 対応オペコード: ldc.i4.*, ldc.i8, ldc.r4, ldc.r8,
    /// add/sub/mul/div/rem, and/or/xor/shl/shr/shr.un/neg/not,
    /// ceq/cgt/cgt.un/clt/clt.un, conv.i4/i8/r4/r8,
    /// br/br.s/brtrue/brfalse/beq/bne/bgt/bge/blt/ble (+.s +.un),
    /// dup/pop/nop/ret。
    /// </summary>
    private static bool TryEvaluateConstantReturn(MethodDef method, out byte tag, out byte[] payload)
    {
        tag = TagNull;
        payload = Array.Empty<byte>();

        var instrs = method.Body.Instructions;
        var stack = new Stack<object>();
        var indexOf = new Dictionary<Instruction, int>();
        for (var i = 0; i < instrs.Count; i++) indexOf[instrs[i]] = i;

        var locals = new object?[method.Body.Variables.Count];

        const int MaxSteps = 1024;
        var steps = 0;
        var pc = 0;

        while (pc < instrs.Count)
        {
            if (++steps > MaxSteps) return false;
            var instr = instrs[pc];
            var op = instr.OpCode.Code;

            switch (op)
            {
                case Code.Nop:
                    pc++;
                    break;

                case Code.Ldc_I4_M1: stack.Push(-1); pc++; break;
                case Code.Ldc_I4_0: stack.Push(0); pc++; break;
                case Code.Ldc_I4_1: stack.Push(1); pc++; break;
                case Code.Ldc_I4_2: stack.Push(2); pc++; break;
                case Code.Ldc_I4_3: stack.Push(3); pc++; break;
                case Code.Ldc_I4_4: stack.Push(4); pc++; break;
                case Code.Ldc_I4_5: stack.Push(5); pc++; break;
                case Code.Ldc_I4_6: stack.Push(6); pc++; break;
                case Code.Ldc_I4_7: stack.Push(7); pc++; break;
                case Code.Ldc_I4_8: stack.Push(8); pc++; break;

                case Code.Ldc_I4:
                case Code.Ldc_I4_S:
                    stack.Push(Convert.ToInt32(instr.Operand));
                    pc++;
                    break;

                case Code.Ldc_I8:
                    stack.Push(Convert.ToInt64(instr.Operand));
                    pc++;
                    break;

                case Code.Ldc_R4:
                    stack.Push(Convert.ToSingle(instr.Operand));
                    pc++;
                    break;

                case Code.Ldc_R8:
                    stack.Push(Convert.ToDouble(instr.Operand));
                    pc++;
                    break;

                case Code.Dup:
                    if (stack.Count == 0) return false;
                    stack.Push(stack.Peek());
                    pc++;
                    break;

                case Code.Pop:
                    if (stack.Count == 0) return false;
                    stack.Pop();
                    pc++;
                    break;

                case Code.Ldloc_0: case Code.Ldloc_1: case Code.Ldloc_2: case Code.Ldloc_3:
                    {
                        var idx = op - Code.Ldloc_0;
                        if (idx >= locals.Length || locals[idx] is null) return false;
                        stack.Push(locals[idx]!);
                        pc++;
                        break;
                    }
                case Code.Ldloc: case Code.Ldloc_S:
                    {
                        var local = instr.Operand as Local;
                        if (local == null) return false;
                        var idx = local.Index;
                        if (idx >= locals.Length || locals[idx] is null) return false;
                        stack.Push(locals[idx]!);
                        pc++;
                        break;
                    }
                case Code.Stloc_0: case Code.Stloc_1: case Code.Stloc_2: case Code.Stloc_3:
                    {
                        if (stack.Count == 0) return false;
                        var idx = op - Code.Stloc_0;
                        if (idx >= locals.Length) return false;
                        locals[idx] = stack.Pop();
                        pc++;
                        break;
                    }
                case Code.Stloc: case Code.Stloc_S:
                    {
                        if (stack.Count == 0) return false;
                        var local = instr.Operand as Local;
                        if (local == null) return false;
                        var idx = local.Index;
                        if (idx >= locals.Length) return false;
                        locals[idx] = stack.Pop();
                        pc++;
                        break;
                    }

                case Code.Add: case Code.Sub: case Code.Mul:
                case Code.Div: case Code.Div_Un:
                case Code.Rem: case Code.Rem_Un:
                case Code.And: case Code.Or: case Code.Xor:
                case Code.Shl: case Code.Shr: case Code.Shr_Un:
                case Code.Ceq: case Code.Cgt: case Code.Cgt_Un:
                case Code.Clt: case Code.Clt_Un:
                case Code.Add_Ovf: case Code.Add_Ovf_Un:
                case Code.Sub_Ovf: case Code.Sub_Ovf_Un:
                case Code.Mul_Ovf: case Code.Mul_Ovf_Un:
                    {
                        if (stack.Count < 2) return false;
                        var b = stack.Pop();
                        var a = stack.Pop();
                        if (!TryBinOp(op, a, b, out var r)) return false;
                        stack.Push(r);
                        pc++;
                        break;
                    }

                case Code.Neg:
                    {
                        if (stack.Count == 0) return false;
                        var a = stack.Pop();
                        stack.Push(a switch
                        {
                            int i => (object)(-i),
                            long l => -l,
                            float f => -f,
                            double d => -d,
                            _ => (object?)null!
                        });
                        if (stack.Peek() is null) return false;
                        pc++;
                        break;
                    }

                case Code.Not:
                    {
                        if (stack.Count == 0) return false;
                        var a = stack.Pop();
                        stack.Push(a switch
                        {
                            int i => (object)(~i),
                            long l => ~l,
                            _ => (object?)null!
                        });
                        if (stack.Peek() is null) return false;
                        pc++;
                        break;
                    }

                case Code.Conv_I4:
                    {
                        if (stack.Count == 0) return false;
                        var a = stack.Pop();
                        stack.Push(Convert.ToInt32(a));
                        pc++;
                        break;
                    }
                case Code.Conv_I8:
                    {
                        if (stack.Count == 0) return false;
                        var a = stack.Pop();
                        stack.Push(Convert.ToInt64(a));
                        pc++;
                        break;
                    }
                case Code.Conv_R4:
                    {
                        if (stack.Count == 0) return false;
                        var a = stack.Pop();
                        stack.Push(Convert.ToSingle(a));
                        pc++;
                        break;
                    }
                case Code.Conv_R8:
                    {
                        if (stack.Count == 0) return false;
                        var a = stack.Pop();
                        stack.Push(Convert.ToDouble(a));
                        pc++;
                        break;
                    }

                case Code.Br:
                case Code.Br_S:
                    {
                        if (instr.Operand is not Instruction target) return false;
                        if (!indexOf.TryGetValue(target, out var ti)) return false;
                        pc = ti;
                        break;
                    }

                case Code.Brtrue:
                case Code.Brtrue_S:
                    {
                        if (stack.Count == 0) return false;
                        var v = stack.Pop();
                        var cond = IsTrue(v);
                        pc = cond
                            ? indexOf[(Instruction)instr.Operand]
                            : pc + 1;
                        break;
                    }
                case Code.Brfalse:
                case Code.Brfalse_S:
                    {
                        if (stack.Count == 0) return false;
                        var v = stack.Pop();
                        var cond = !IsTrue(v);
                        pc = cond
                            ? indexOf[(Instruction)instr.Operand]
                            : pc + 1;
                        break;
                    }
                case Code.Beq: case Code.Beq_S:
                case Code.Bne_Un: case Code.Bne_Un_S:
                case Code.Bgt: case Code.Bgt_S: case Code.Bgt_Un: case Code.Bgt_Un_S:
                case Code.Bge: case Code.Bge_S: case Code.Bge_Un: case Code.Bge_Un_S:
                case Code.Blt: case Code.Blt_S: case Code.Blt_Un: case Code.Blt_Un_S:
                case Code.Ble: case Code.Ble_S: case Code.Ble_Un: case Code.Ble_Un_S:
                    {
                        if (stack.Count < 2) return false;
                        var b = stack.Pop();
                        var a = stack.Pop();
                        if (!TryCompareBranch(op, a, b, out var cond)) return false;
                        pc = cond
                            ? indexOf[(Instruction)instr.Operand]
                            : pc + 1;
                        break;
                    }

                case Code.Ret:
                    {
                        var retType = method.ReturnType.FullName;
                        if (retType == "System.Void")
                        {
                            tag = TagNull;
                            payload = Array.Empty<byte>();
                            return true;
                        }
                        if (stack.Count == 0) return false;
                        var v = stack.Pop();
                        return TryEncodeReturn(retType, v, out tag, out payload);
                    }

                default:
                    return false;
            }
        }

        return false;
    }

    private static bool TryBinOp(Code op, object a, object b, out object r)
    {
        // 両オペランドを共通数値型に昇格
        if (a is int ai && b is int bi)
        {
            r = op switch
            {
                Code.Add or Code.Add_Ovf or Code.Add_Ovf_Un => (object)(ai + bi),
                Code.Sub or Code.Sub_Ovf or Code.Sub_Ovf_Un => ai - bi,
                Code.Mul or Code.Mul_Ovf or Code.Mul_Ovf_Un => ai * bi,
                Code.Div => bi == 0 ? 0 : ai / bi,
                Code.Div_Un => bi == 0 ? 0 : (int)((uint)ai / (uint)bi),
                Code.Rem => bi == 0 ? 0 : ai % bi,
                Code.Rem_Un => bi == 0 ? 0 : (int)((uint)ai % (uint)bi),
                Code.And => ai & bi,
                Code.Or => ai | bi,
                Code.Xor => ai ^ bi,
                Code.Shl => ai << (bi & 0x1f),
                Code.Shr => ai >> (bi & 0x1f),
                Code.Shr_Un => (int)((uint)ai >> (bi & 0x1f)),
                Code.Ceq => ai == bi ? 1 : 0,
                Code.Cgt => ai > bi ? 1 : 0,
                Code.Cgt_Un => (uint)ai > (uint)bi ? 1 : 0,
                Code.Clt => ai < bi ? 1 : 0,
                Code.Clt_Un => (uint)ai < (uint)bi ? 1 : 0,
                _ => null!
            };
            return r != null;
        }
        if (a is long || b is long)
        {
            var la = Convert.ToInt64(a);
            var lb = Convert.ToInt64(b);
            r = op switch
            {
                Code.Add or Code.Add_Ovf or Code.Add_Ovf_Un => (object)(la + lb),
                Code.Sub or Code.Sub_Ovf or Code.Sub_Ovf_Un => la - lb,
                Code.Mul or Code.Mul_Ovf or Code.Mul_Ovf_Un => la * lb,
                Code.Div => lb == 0 ? 0L : la / lb,
                Code.Rem => lb == 0 ? 0L : la % lb,
                Code.And => la & lb,
                Code.Or => la | lb,
                Code.Xor => la ^ lb,
                Code.Shl => la << (int)(lb & 0x3f),
                Code.Shr => la >> (int)(lb & 0x3f),
                Code.Shr_Un => (long)((ulong)la >> (int)(lb & 0x3f)),
                Code.Ceq => la == lb ? 1 : 0,
                Code.Cgt => la > lb ? 1 : 0,
                Code.Clt => la < lb ? 1 : 0,
                _ => null!
            };
            return r != null;
        }
        if (a is double || b is double)
        {
            var da = Convert.ToDouble(a);
            var db = Convert.ToDouble(b);
            r = op switch
            {
                Code.Add => (object)(da + db),
                Code.Sub => da - db,
                Code.Mul => da * db,
                Code.Div => da / db,
                Code.Ceq => da == db ? 1 : 0,
                Code.Cgt => da > db ? 1 : 0,
                Code.Clt => da < db ? 1 : 0,
                _ => null!
            };
            return r != null;
        }
        if (a is float || b is float)
        {
            var fa = Convert.ToSingle(a);
            var fb = Convert.ToSingle(b);
            r = op switch
            {
                Code.Add => (object)(fa + fb),
                Code.Sub => fa - fb,
                Code.Mul => fa * fb,
                Code.Div => fa / fb,
                _ => null!
            };
            return r != null;
        }
        r = null!;
        return false;
    }

    private static bool TryCompareBranch(Code op, object a, object b, out bool cond)
    {
        cond = false;
        if (a is int ai && b is int bi)
        {
            cond = op switch
            {
                Code.Beq or Code.Beq_S => ai == bi,
                Code.Bne_Un or Code.Bne_Un_S => ai != bi,
                Code.Bgt or Code.Bgt_S => ai > bi,
                Code.Bgt_Un or Code.Bgt_Un_S => (uint)ai > (uint)bi,
                Code.Bge or Code.Bge_S => ai >= bi,
                Code.Bge_Un or Code.Bge_Un_S => (uint)ai >= (uint)bi,
                Code.Blt or Code.Blt_S => ai < bi,
                Code.Blt_Un or Code.Blt_Un_S => (uint)ai < (uint)bi,
                Code.Ble or Code.Ble_S => ai <= bi,
                Code.Ble_Un or Code.Ble_Un_S => (uint)ai <= (uint)bi,
                _ => false
            };
            return true;
        }
        if (a is long || b is long)
        {
            var la = Convert.ToInt64(a);
            var lb = Convert.ToInt64(b);
            cond = op switch
            {
                Code.Beq or Code.Beq_S => la == lb,
                Code.Bne_Un or Code.Bne_Un_S => la != lb,
                Code.Bgt or Code.Bgt_S => la > lb,
                Code.Bge or Code.Bge_S => la >= lb,
                Code.Blt or Code.Blt_S => la < lb,
                Code.Ble or Code.Ble_S => la <= lb,
                _ => false
            };
            return true;
        }
        return false;
    }

    private static bool IsTrue(object v)
    {
        return v switch
        {
            int i => i != 0,
            long l => l != 0,
            float f => f != 0,
            double d => d != 0,
            _ => false
        };
    }

    private static bool TryEncodeReturn(string retTypeFullName, object v, out byte tag, out byte[] payload)
    {
        switch (retTypeFullName)
        {
            case "System.Int32":
            case "System.UInt32":
                tag = TagInt32;
                payload = BitConverter.GetBytes(Convert.ToInt32(v));
                return true;
            case "System.Int64":
            case "System.UInt64":
                tag = TagInt64;
                payload = BitConverter.GetBytes(Convert.ToInt64(v));
                return true;
            case "System.Boolean":
                tag = TagBool;
                payload = new[] { (byte)(IsTrue(v) ? 1 : 0) };
                return true;
            case "System.Single":
                tag = TagSingle;
                payload = BitConverter.GetBytes(Convert.ToSingle(v));
                return true;
            case "System.Double":
                tag = TagDouble;
                payload = BitConverter.GetBytes(Convert.ToDouble(v));
                return true;
            default:
                tag = TagNull;
                payload = Array.Empty<byte>();
                return false;
        }
    }

    private static byte[] SerializeEntries(List<(int id, byte tag, byte[] payload)> entries)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write(entries.Count);
        foreach (var (id, tag, payload) in entries)
        {
            writer.Write(id);
            writer.Write(tag);
            writer.Write(payload.Length);
            writer.Write(payload);
        }

        var raw = ms.ToArray();
        // XOR 暗号化
        for (var i = 0; i < raw.Length; i++)
            raw[i] ^= XorKey;
        return raw;
    }

    private static void ReplaceBodyWithStub(
        MethodDef method, ModuleDef module,
        MethodDef restoreMethod, int methodId)
    {
        var body = new CilBody();
        method.Body = body;
        body.InitLocals = true;

        // R(methodId, new object[0])
        body.Instructions.Add(Instruction.CreateLdcI4(methodId));
        body.Instructions.Add(Instruction.CreateLdcI4(0));
        body.Instructions.Add(new Instruction(OpCodes.Newarr, module.CorLibTypes.Object.TypeDefOrRef));
        body.Instructions.Add(new Instruction(OpCodes.Call, restoreMethod));

        // 戻り値を処理
        var retType = method.ReturnType;
        if (retType.FullName == "System.Void")
            body.Instructions.Add(new Instruction(OpCodes.Pop));
        else if (retType.IsValueType)
            body.Instructions.Add(new Instruction(OpCodes.Unbox_Any, retType.ToTypeDefOrRef()));
        else if (retType.FullName != "System.Object")
            body.Instructions.Add(new Instruction(OpCodes.Castclass, retType.ToTypeDefOrRef()));

        body.Instructions.Add(new Instruction(OpCodes.Ret));
    }

    private static TypeDefUser InjectNecroBitHelper(ModuleDef module, Importer importer)
    {
        var helperType = new TypeDefUser("", "\u200C\u200D\u200B", module.CorLibTypes.Object.TypeDefOrRef);
        helperType.Attributes = dnlib.DotNet.TypeAttributes.NotPublic | dnlib.DotNet.TypeAttributes.Sealed
            | dnlib.DotNet.TypeAttributes.Abstract;

        var objectArraySig = new SZArraySig(module.CorLibTypes.Object);
        var restoreMethod = new MethodDefUser(
            "R",
            MethodSig.CreateStatic(module.CorLibTypes.Object, module.CorLibTypes.Int32, objectArraySig),
            dnlib.DotNet.MethodImplAttributes.IL | dnlib.DotNet.MethodImplAttributes.Managed,
            dnlib.DotNet.MethodAttributes.Public | dnlib.DotNet.MethodAttributes.Static
                | dnlib.DotNet.MethodAttributes.HideBySig);

        var body = new CilBody();
        restoreMethod.Body = body;
        body.InitLocals = true;

        // Locals
        var localStream = new Local(importer.ImportAsTypeSig(typeof(Stream)));    // 0
        var localReader = new Local(importer.ImportAsTypeSig(typeof(BinaryReader))); // 1
        var localCount = new Local(module.CorLibTypes.Int32);                      // 2
        var localI = new Local(module.CorLibTypes.Int32);                          // 3
        var localCurrentId = new Local(module.CorLibTypes.Int32);                  // 4
        var localTag = new Local(module.CorLibTypes.Int32);                        // 5 (byte promoted to int)
        var localLen = new Local(module.CorLibTypes.Int32);                        // 6
        var localBuf = new Local(new SZArraySig(module.CorLibTypes.Byte));          // 7
        var localResult = new Local(module.CorLibTypes.Object);                    // 8
        var localRaw = new Local(new SZArraySig(module.CorLibTypes.Byte));          // 9 raw resource (xor)
        var localRawLen = new Local(module.CorLibTypes.Int32);                     // 10
        var localJ = new Local(module.CorLibTypes.Int32);                          // 11
        body.Variables.Add(localStream);
        body.Variables.Add(localReader);
        body.Variables.Add(localCount);
        body.Variables.Add(localI);
        body.Variables.Add(localCurrentId);
        body.Variables.Add(localTag);
        body.Variables.Add(localLen);
        body.Variables.Add(localBuf);
        body.Variables.Add(localResult);
        body.Variables.Add(localRaw);
        body.Variables.Add(localRawLen);
        body.Variables.Add(localJ);

        // Imports
        var getExecAsm = importer.Import(
            typeof(System.Reflection.Assembly).GetMethod("GetExecutingAssembly")!);
        var getManifestStream = importer.Import(
            typeof(System.Reflection.Assembly).GetMethod("GetManifestResourceStream", [typeof(string)])!);
        var streamLength = importer.Import(typeof(Stream).GetProperty("Length")!.GetMethod!);
        var streamRead = importer.Import(typeof(Stream).GetMethod("Read", [typeof(byte[]), typeof(int), typeof(int)])!);
        var memoryStreamCtor = importer.Import(typeof(MemoryStream).GetConstructor([typeof(byte[])])!);
        var brCtor = importer.Import(typeof(BinaryReader).GetConstructor([typeof(Stream)])!);
        var readInt32 = importer.Import(typeof(BinaryReader).GetMethod("ReadInt32")!);
        var readByte = importer.Import(typeof(BinaryReader).GetMethod("ReadByte")!);
        var readBytes = importer.Import(typeof(BinaryReader).GetMethod("ReadBytes", [typeof(int)])!);

        var bitConverterToInt32 = importer.Import(typeof(BitConverter).GetMethod("ToInt32", [typeof(byte[]), typeof(int)])!);
        var bitConverterToInt64 = importer.Import(typeof(BitConverter).GetMethod("ToInt64", [typeof(byte[]), typeof(int)])!);
        var bitConverterToSingle = importer.Import(typeof(BitConverter).GetMethod("ToSingle", [typeof(byte[]), typeof(int)])!);
        var bitConverterToDouble = importer.Import(typeof(BitConverter).GetMethod("ToDouble", [typeof(byte[]), typeof(int)])!);

        var instrs = body.Instructions;

        // --- Read raw bytes from resource stream ---
        // stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
        instrs.Add(OpCodes.Call.ToInstruction(getExecAsm));
        instrs.Add(OpCodes.Ldstr.ToInstruction(ResourceName));
        instrs.Add(OpCodes.Callvirt.ToInstruction(getManifestStream));
        instrs.Add(OpCodes.Stloc.ToInstruction(localStream));

        // if (stream == null) return null;
        var afterStreamCheck = OpCodes.Ldloc.ToInstruction(localStream);
        instrs.Add(OpCodes.Ldloc.ToInstruction(localStream));
        instrs.Add(OpCodes.Brtrue.ToInstruction(afterStreamCheck));
        instrs.Add(OpCodes.Ldnull.ToInstruction());
        instrs.Add(OpCodes.Ret.ToInstruction());

        // rawLen = (int)stream.Length
        instrs.Add(afterStreamCheck);
        instrs.Add(OpCodes.Callvirt.ToInstruction(streamLength));
        instrs.Add(OpCodes.Conv_I4.ToInstruction());
        instrs.Add(OpCodes.Stloc.ToInstruction(localRawLen));

        // raw = new byte[rawLen]
        instrs.Add(OpCodes.Ldloc.ToInstruction(localRawLen));
        instrs.Add(OpCodes.Newarr.ToInstruction(module.CorLibTypes.Byte.TypeDefOrRef));
        instrs.Add(OpCodes.Stloc.ToInstruction(localRaw));

        // stream.Read(raw, 0, rawLen); (pop result)
        instrs.Add(OpCodes.Ldloc.ToInstruction(localStream));
        instrs.Add(OpCodes.Ldloc.ToInstruction(localRaw));
        instrs.Add(OpCodes.Ldc_I4_0.ToInstruction());
        instrs.Add(OpCodes.Ldloc.ToInstruction(localRawLen));
        instrs.Add(OpCodes.Callvirt.ToInstruction(streamRead));
        instrs.Add(OpCodes.Pop.ToInstruction());

        // XOR decrypt raw in-place
        // for (j = 0; j < rawLen; j++) raw[j] ^= XorKey;
        instrs.Add(OpCodes.Ldc_I4_0.ToInstruction());
        instrs.Add(OpCodes.Stloc.ToInstruction(localJ));
        var xorCheck = OpCodes.Ldloc.ToInstruction(localJ);
        var xorBody = OpCodes.Ldloc.ToInstruction(localRaw);
        instrs.Add(OpCodes.Br.ToInstruction(xorCheck));
        instrs.Add(xorBody);
        instrs.Add(OpCodes.Ldloc.ToInstruction(localJ));
        instrs.Add(OpCodes.Ldelema.ToInstruction(module.CorLibTypes.Byte.TypeDefOrRef));
        instrs.Add(OpCodes.Dup.ToInstruction());
        instrs.Add(OpCodes.Ldind_U1.ToInstruction());
        instrs.Add(OpCodes.Ldc_I4.ToInstruction((int)XorKey));
        instrs.Add(OpCodes.Xor.ToInstruction());
        instrs.Add(OpCodes.Conv_U1.ToInstruction());
        instrs.Add(OpCodes.Stind_I1.ToInstruction());
        instrs.Add(OpCodes.Ldloc.ToInstruction(localJ));
        instrs.Add(OpCodes.Ldc_I4_1.ToInstruction());
        instrs.Add(OpCodes.Add.ToInstruction());
        instrs.Add(OpCodes.Stloc.ToInstruction(localJ));
        instrs.Add(xorCheck);
        instrs.Add(OpCodes.Ldloc.ToInstruction(localRawLen));
        instrs.Add(OpCodes.Blt.ToInstruction(xorBody));

        // reader = new BinaryReader(new MemoryStream(raw))
        instrs.Add(OpCodes.Ldloc.ToInstruction(localRaw));
        instrs.Add(OpCodes.Newobj.ToInstruction(memoryStreamCtor));
        instrs.Add(OpCodes.Newobj.ToInstruction(brCtor));
        instrs.Add(OpCodes.Stloc.ToInstruction(localReader));

        // count = reader.ReadInt32()
        instrs.Add(OpCodes.Ldloc.ToInstruction(localReader));
        instrs.Add(OpCodes.Callvirt.ToInstruction(readInt32));
        instrs.Add(OpCodes.Stloc.ToInstruction(localCount));

        // for (i = 0; i < count; i++)
        instrs.Add(OpCodes.Ldc_I4_0.ToInstruction());
        instrs.Add(OpCodes.Stloc.ToInstruction(localI));

        var loopCheck = OpCodes.Ldloc.ToInstruction(localI);
        var loopBody = OpCodes.Ldloc.ToInstruction(localReader);
        instrs.Add(OpCodes.Br.ToInstruction(loopCheck));

        // currentId = reader.ReadInt32()
        instrs.Add(loopBody);
        instrs.Add(OpCodes.Callvirt.ToInstruction(readInt32));
        instrs.Add(OpCodes.Stloc.ToInstruction(localCurrentId));

        // tag = reader.ReadByte()
        instrs.Add(OpCodes.Ldloc.ToInstruction(localReader));
        instrs.Add(OpCodes.Callvirt.ToInstruction(readByte));
        instrs.Add(OpCodes.Stloc.ToInstruction(localTag));

        // len = reader.ReadInt32()
        instrs.Add(OpCodes.Ldloc.ToInstruction(localReader));
        instrs.Add(OpCodes.Callvirt.ToInstruction(readInt32));
        instrs.Add(OpCodes.Stloc.ToInstruction(localLen));

        // buf = reader.ReadBytes(len)
        instrs.Add(OpCodes.Ldloc.ToInstruction(localReader));
        instrs.Add(OpCodes.Ldloc.ToInstruction(localLen));
        instrs.Add(OpCodes.Callvirt.ToInstruction(readBytes));
        instrs.Add(OpCodes.Stloc.ToInstruction(localBuf));

        // if (currentId != arg0) goto skip
        var skipLabel = OpCodes.Ldloc.ToInstruction(localI);
        instrs.Add(OpCodes.Ldloc.ToInstruction(localCurrentId));
        instrs.Add(OpCodes.Ldarg_0.ToInstruction());
        instrs.Add(OpCodes.Bne_Un.ToInstruction(skipLabel));

        // マッチ: tag に応じてデコードして返す
        // switch (tag) ...
        var tagNullLabel = OpCodes.Ldnull.ToInstruction();
        var tagInt32Label = OpCodes.Ldloc.ToInstruction(localBuf);
        var tagInt64Label = OpCodes.Ldloc.ToInstruction(localBuf);
        var tagBoolLabel = OpCodes.Ldloc.ToInstruction(localBuf);
        var tagSingleLabel = OpCodes.Ldloc.ToInstruction(localBuf);
        var tagDoubleLabel = OpCodes.Ldloc.ToInstruction(localBuf);

        // if (tag == TagNull) goto nullLabel
        instrs.Add(OpCodes.Ldloc.ToInstruction(localTag));
        instrs.Add(OpCodes.Ldc_I4.ToInstruction((int)TagNull));
        instrs.Add(OpCodes.Beq.ToInstruction(tagNullLabel));
        // if (tag == TagInt32) goto ...
        instrs.Add(OpCodes.Ldloc.ToInstruction(localTag));
        instrs.Add(OpCodes.Ldc_I4.ToInstruction((int)TagInt32));
        instrs.Add(OpCodes.Beq.ToInstruction(tagInt32Label));
        instrs.Add(OpCodes.Ldloc.ToInstruction(localTag));
        instrs.Add(OpCodes.Ldc_I4.ToInstruction((int)TagInt64));
        instrs.Add(OpCodes.Beq.ToInstruction(tagInt64Label));
        instrs.Add(OpCodes.Ldloc.ToInstruction(localTag));
        instrs.Add(OpCodes.Ldc_I4.ToInstruction((int)TagBool));
        instrs.Add(OpCodes.Beq.ToInstruction(tagBoolLabel));
        instrs.Add(OpCodes.Ldloc.ToInstruction(localTag));
        instrs.Add(OpCodes.Ldc_I4.ToInstruction((int)TagSingle));
        instrs.Add(OpCodes.Beq.ToInstruction(tagSingleLabel));
        instrs.Add(OpCodes.Ldloc.ToInstruction(localTag));
        instrs.Add(OpCodes.Ldc_I4.ToInstruction((int)TagDouble));
        instrs.Add(OpCodes.Beq.ToInstruction(tagDoubleLabel));
        // デフォルト: null
        instrs.Add(OpCodes.Br.ToInstruction(tagNullLabel));

        // TagNull: return null
        instrs.Add(tagNullLabel);
        instrs.Add(OpCodes.Ret.ToInstruction());

        // TagInt32: return (object)BitConverter.ToInt32(buf, 0)
        instrs.Add(tagInt32Label);
        instrs.Add(OpCodes.Ldc_I4_0.ToInstruction());
        instrs.Add(OpCodes.Call.ToInstruction(bitConverterToInt32));
        instrs.Add(OpCodes.Box.ToInstruction(module.CorLibTypes.Int32.TypeDefOrRef));
        instrs.Add(OpCodes.Ret.ToInstruction());

        // TagInt64
        instrs.Add(tagInt64Label);
        instrs.Add(OpCodes.Ldc_I4_0.ToInstruction());
        instrs.Add(OpCodes.Call.ToInstruction(bitConverterToInt64));
        instrs.Add(OpCodes.Box.ToInstruction(module.CorLibTypes.Int64.TypeDefOrRef));
        instrs.Add(OpCodes.Ret.ToInstruction());

        // TagBool: return (object)(buf[0] != 0)
        instrs.Add(tagBoolLabel);
        instrs.Add(OpCodes.Ldc_I4_0.ToInstruction());
        instrs.Add(OpCodes.Ldelem_U1.ToInstruction());
        instrs.Add(OpCodes.Ldc_I4_0.ToInstruction());
        instrs.Add(OpCodes.Cgt_Un.ToInstruction());
        instrs.Add(OpCodes.Box.ToInstruction(module.CorLibTypes.Boolean.TypeDefOrRef));
        instrs.Add(OpCodes.Ret.ToInstruction());

        // TagSingle
        instrs.Add(tagSingleLabel);
        instrs.Add(OpCodes.Ldc_I4_0.ToInstruction());
        instrs.Add(OpCodes.Call.ToInstruction(bitConverterToSingle));
        instrs.Add(OpCodes.Box.ToInstruction(module.CorLibTypes.Single.TypeDefOrRef));
        instrs.Add(OpCodes.Ret.ToInstruction());

        // TagDouble
        instrs.Add(tagDoubleLabel);
        instrs.Add(OpCodes.Ldc_I4_0.ToInstruction());
        instrs.Add(OpCodes.Call.ToInstruction(bitConverterToDouble));
        instrs.Add(OpCodes.Box.ToInstruction(module.CorLibTypes.Double.TypeDefOrRef));
        instrs.Add(OpCodes.Ret.ToInstruction());

        // skip: i++; goto loopCheck
        instrs.Add(skipLabel);
        instrs.Add(OpCodes.Ldc_I4_1.ToInstruction());
        instrs.Add(OpCodes.Add.ToInstruction());
        instrs.Add(OpCodes.Stloc.ToInstruction(localI));

        instrs.Add(loopCheck);
        instrs.Add(OpCodes.Ldloc.ToInstruction(localCount));
        instrs.Add(OpCodes.Blt.ToInstruction(loopBody));

        // not found: return null
        instrs.Add(OpCodes.Ldnull.ToInstruction());
        instrs.Add(OpCodes.Ret.ToInstruction());

        helperType.Methods.Add(restoreMethod);
        module.Types.Add(helperType);

        return helperType;
    }
}
