using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Netbfsctn.Core.Pipeline;
using Netbfsctn.Core.Techniques;

namespace Netbfsctn.IL.Techniques;

public class ILControlFlowObfuscator : IObfuscationTechnique<ModuleDef>
{
    public string Name => "制御フロー難読化 (IL)";

    public void Apply(ModuleDef module, ObfuscationContext context, ObfuscationResult result)
    {
        foreach (var type in module.GetTypes())
        {
            if (type.Name == "<Module>")
                continue;
            // 他のテクニックが注入したヘルパー型はスキップ
            if (IsInjectedHelperType(type.Name))
                continue;

            foreach (var method in type.Methods)
            {
                if (!method.HasBody) continue;
                if (method.Body.Instructions.Count < 4) continue;
                if (method.Body.ExceptionHandlers.Count > 0) continue;
                // switch 命令を含むメソッドはスキップ（ステートマシン変換と衝突する）
                if (method.Body.Instructions.Any(i => i.OpCode == OpCodes.Switch)) continue;

                var hasReturnValue = method.ReturnType != null
                    && method.ReturnType.ElementType != ElementType.Void;
                try
                {
                    TransformToStatesMachine(method, module, context, hasReturnValue);
                    result.ObfuscatedMethods++;
                }
                catch (Exception ex)
                {
                    context.Logger.Verbose($"制御フロー変換をスキップ: {method.FullName} ({ex.Message})");
                }
            }
        }
    }

    private void TransformToStatesMachine(MethodDef method, ModuleDef module, ObfuscationContext context, bool hasReturnValue)
    {
        var body = method.Body;

        // 基本ブロックに分割
        var blocks = SplitIntoBasicBlocks(body);
        if (blocks.Count < 2)
            return;

        // スタック非中立なブロックを隣接ブロックと結合してスタック中立にする
        blocks = MergeToStackNeutralBlocks(blocks, hasReturnValue);
        if (blocks.Count < 2)
            return;

        // 全ブロックがスタック中立であることを検証（ret/throw で終わるブロックを除く）
        foreach (var block in blocks)
        {
            var lastOp = block[^1].OpCode;
            if (lastOp == OpCodes.Ret || lastOp == OpCodes.Throw)
                continue;
            if (CalculateBlockStackDelta(block, hasReturnValue) != 0)
                return; // スタック中立にできないメソッドは変換をスキップ
        }

        // ブロック内の命令 → ブロックインデックスのマップ
        var preInstrToBlock = new Dictionary<Instruction, int>();
        for (var bi = 0; bi < blocks.Count; bi++)
            foreach (var blockInstr in blocks[bi])
                preInstrToBlock[blockInstr] = bi;

        // クロスブロック分岐がスタック深度 0 で発生することを検証。
        // 非ゼロ深度でのクロスブロック分岐は状態マシン経由にできない。
        foreach (var block in blocks)
        {
            var depth = 0;
            foreach (var instr in block)
            {
                // 分岐命令の場合、ターゲットが別ブロックなら深度チェック
                if (instr.OpCode.FlowControl is FlowControl.Cond_Branch or FlowControl.Branch)
                {
                    if (instr.Operand is Instruction target
                        && preInstrToBlock.TryGetValue(target, out var targetBi)
                        && preInstrToBlock.TryGetValue(instr, out var sourceBi)
                        && targetBi != sourceBi
                        && depth != 0)
                    {
                        return; // 非ゼロ深度のクロスブロック分岐あり → スキップ
                    }
                }

                depth -= EstimateStackPop(instr, hasReturnValue);
                if (depth < 0) depth = 0;
                depth += EstimateStackPush(instr);
            }
        }

        context.Logger.Verbose($"制御フロー変換: {method.Name} ({blocks.Count} ブロック)");

        // ステート変数を追加
        var stateVar = new Local(module.CorLibTypes.Int32);
        body.Variables.Add(stateVar);
        body.InitLocals = true;
        var stateIndex = body.Variables.Count - 1;

        // ブロック順序をシャッフル用のマッピング作成
        var random = new Random(method.FullName.GetHashCode());
        var order = Enumerable.Range(0, blocks.Count).ToList();
        for (var i = order.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        // 新しい命令リスト構築
        var newInstructions = new List<Instruction>();

        // state = 0 (最初のブロックのインデックス)
        newInstructions.Add(Instruction.CreateLdcI4(0));
        newInstructions.Add(new Instruction(OpCodes.Stloc, body.Variables[stateIndex]));

        // ループ開始点
        var loopStart = new Instruction(OpCodes.Ldloc, body.Variables[stateIndex]);
        newInstructions.Add(loopStart);

        // switch ディスパッチャー
        var blockLabels = new Instruction[blocks.Count];
        for (var i = 0; i < blocks.Count; i++)
        {
            blockLabels[i] = new Instruction(OpCodes.Nop);
        }

        newInstructions.Add(new Instruction(OpCodes.Switch, blockLabels));

        // デフォルトケース (到達しないはず) - ldnull + throw で CLR 検証を通す
        // ret を使うと戻り値ありメソッドでスタック不整合になる
        var exitLabel = new Instruction(OpCodes.Ldnull);
        var exitThrow = new Instruction(OpCodes.Throw);
        newInstructions.Add(new Instruction(OpCodes.Br, exitLabel));

        // 全命令 → 所属するブロックインデックスのマップ構築
        // （結合前のブロック先頭だけでなく、全命令をカバー）
        var instrToBlockIdx = new Dictionary<Instruction, int>();
        for (var i = 0; i < blocks.Count; i++)
        {
            foreach (var blockInstr in blocks[i])
            {
                instrToBlockIdx[blockInstr] = i;
            }
        }

        // 各ブロックをシャッフルされた順序で配置
        for (var shuffledIdx = 0; shuffledIdx < blocks.Count; shuffledIdx++)
        {
            var originalIdx = order[shuffledIdx];
            var block = blocks[originalIdx];

            newInstructions.Add(blockLabels[originalIdx]);

            // ブロックの命令をコピー
            for (var i = 0; i < block.Count; i++)
            {
                var instr = block[i];
                newInstructions.Add(instr);
            }

            // 次のブロックへのステート遷移を追加（ret で終わらない場合）
            var lastInstr = block[^1];
            if (lastInstr.OpCode != OpCodes.Ret && lastInstr.OpCode != OpCodes.Throw)
            {
                var nextState = originalIdx + 1;
                if (nextState < blocks.Count)
                {
                    newInstructions.Add(Instruction.CreateLdcI4(nextState));
                    newInstructions.Add(new Instruction(OpCodes.Stloc, body.Variables[stateIndex]));
                    newInstructions.Add(new Instruction(OpCodes.Br, loopStart));
                }
            }
        }

        newInstructions.Add(exitLabel);
        newInstructions.Add(exitThrow);

        // 命令を置換
        body.Instructions.Clear();
        foreach (var instr in newInstructions)
        {
            body.Instructions.Add(instr);
        }

        // クロスケースブランチをステートマシン経由に書き換え:
        // 元の分岐ターゲットが他ブロックの先頭を参照している場合、
        // そのブロックの状態番号を設定してディスパッチャに戻るように変換する。
        // 条件分岐はフォールスルーを壊さないようアウトオブラインに配置する。
        var outOfLineBlocks = new List<Instruction>();
        var instrCount = body.Instructions.Count; // 元の命令数を記録
        for (var i = 0; i < instrCount; i++)
        {
            var instr = body.Instructions[i];

            // この命令が所属するブロックを特定
            instrToBlockIdx.TryGetValue(instr, out var currentBlockIdx);

            // 条件分岐: ターゲットが別ブロックなら書き換え
            // フォールスルーを壊さないよう、状態設定コードをメソッド末尾に配置
            if (instr.OpCode.FlowControl == FlowControl.Cond_Branch
                && instr.Operand is Instruction condTarget
                && instrToBlockIdx.TryGetValue(condTarget, out var targetBlockIdx)
                && targetBlockIdx != currentBlockIdx)
            {
                var stateSet = Instruction.CreateLdcI4(targetBlockIdx);
                instr.Operand = stateSet;

                // アウトオブラインブロック: ldc.i4 state → stloc → br loopStart
                outOfLineBlocks.Add(stateSet);
                outOfLineBlocks.Add(new Instruction(OpCodes.Stloc, body.Variables[stateIndex]));
                outOfLineBlocks.Add(new Instruction(OpCodes.Br, loopStart));
            }

            // 無条件分岐: ターゲットが別ブロックなら書き換え
            // インラインで置換可（フォールスルーなし）
            if (instr.OpCode.FlowControl == FlowControl.Branch
                && instr.OpCode.Code != Code.Leave && instr.OpCode.Code != Code.Leave_S
                && instr.Operand is Instruction brTarget
                && instrToBlockIdx.TryGetValue(brTarget, out var brTargetBlockIdx)
                && brTargetBlockIdx != currentBlockIdx)
            {
                instr.OpCode = OpCodes.Ldc_I4;
                instr.Operand = brTargetBlockIdx;

                var stateStore = new Instruction(OpCodes.Stloc, body.Variables[stateIndex]);
                var brToLoop = new Instruction(OpCodes.Br, loopStart);
                body.Instructions.Insert(i + 1, stateStore);
                body.Instructions.Insert(i + 2, brToLoop);
                instrCount += 2;
                i += 2;
            }
        }

        // アウトオブラインブロックをメソッド末尾に追加
        foreach (var outInstr in outOfLineBlocks)
        {
            body.Instructions.Add(outInstr);
        }

        // dnlib の maxStack 再計算は状態マシンの複雑な制御フローで失敗するためバイパス。
        // 正確な maxStack は ILObfuscationPipeline が全テクニック適用後に計算する。
        body.KeepOldMaxStack = true;
    }

    private static List<List<Instruction>> MergeToStackNeutralBlocks(List<List<Instruction>> blocks, bool hasReturnValue)
    {
        var merged = new List<List<Instruction>>();
        var current = new List<Instruction>(blocks[0]);

        for (var i = 1; i < blocks.Count; i++)
        {
            if (CalculateBlockStackDelta(current, hasReturnValue) == 0)
            {
                // 現在のブロックはスタック中立なので確定
                merged.Add(current);
                current = new List<Instruction>(blocks[i]);
            }
            else
            {
                // スタック非中立なので次のブロックと結合
                current.AddRange(blocks[i]);
            }
        }

        // 最後のブロック
        if (current.Count > 0)
        {
            if (CalculateBlockStackDelta(current, hasReturnValue) != 0)
            {
                // 最後のブロックが非中立なら前のブロックと結合
                if (merged.Count > 0)
                    merged[^1].AddRange(current);
                else
                    merged.Add(current);
            }
            else
            {
                merged.Add(current);
            }
        }

        return merged;
    }

    private static List<List<Instruction>> SplitIntoBasicBlocks(CilBody body)
    {
        var blocks = new List<List<Instruction>>();
        var currentBlock = new List<Instruction>();

        // 分岐先のオフセットを収集
        var branchTargets = new HashSet<Instruction>();
        foreach (var instr in body.Instructions)
        {
            if (instr.Operand is Instruction target)
                branchTargets.Add(target);
            if (instr.Operand is Instruction[] targets)
                foreach (var t in targets)
                    branchTargets.Add(t);
        }

        foreach (var instr in body.Instructions)
        {
            // 分岐先ならブロック境界
            if (branchTargets.Contains(instr) && currentBlock.Count > 0)
            {
                blocks.Add(currentBlock);
                currentBlock = [];
            }

            currentBlock.Add(instr);

            // 分岐/ret ならブロック末尾
            if (instr.OpCode.FlowControl is FlowControl.Branch
                or FlowControl.Cond_Branch
                or FlowControl.Return
                or FlowControl.Throw)
            {
                blocks.Add(currentBlock);
                currentBlock = [];
            }
        }

        if (currentBlock.Count > 0)
            blocks.Add(currentBlock);

        return blocks;
    }

    private static int CalculateBlockStackDelta(List<Instruction> block, bool hasReturnValue)
    {
        var delta = 0;
        foreach (var instr in block)
        {
            delta -= EstimateStackPop(instr, hasReturnValue);
            delta += EstimateStackPush(instr);
        }
        return delta;
    }

    private static int EstimateStackPop(Instruction instr, bool hasReturnValue)
    {
        var opCode = instr.OpCode;

        // call/callvirt/newobj は引数の数に依存
        if (opCode.Code is Code.Call or Code.Callvirt or Code.Newobj)
        {
            var sig = GetMethodSig(instr);
            if (sig != null)
            {
                var count = sig.Params.Count;
                // インスタンスメソッドは this を消費
                if (sig.HasThis && opCode.Code != Code.Newobj)
                    count++;
                return count;
            }
            return 0;
        }

        // ret は Varpop: 戻り値があるメソッドでは1つポップ、void なら0
        if (opCode.Code == Code.Ret)
            return hasReturnValue ? 1 : 0;

        return opCode.StackBehaviourPop switch
        {
            StackBehaviour.Pop0 => 0,
            StackBehaviour.Pop1 or StackBehaviour.Popi or StackBehaviour.Popref => 1,
            StackBehaviour.Pop1_pop1 or StackBehaviour.Popi_pop1
                or StackBehaviour.Popi_popi or StackBehaviour.Popi_popi8
                or StackBehaviour.Popi_popr4 or StackBehaviour.Popi_popr8
                or StackBehaviour.Popref_pop1 or StackBehaviour.Popref_popi => 2,
            StackBehaviour.Popi_popi_popi or StackBehaviour.Popref_popi_popi
                or StackBehaviour.Popref_popi_popi8 or StackBehaviour.Popref_popi_popr4
                or StackBehaviour.Popref_popi_popr8 or StackBehaviour.Popref_popi_popref
                or StackBehaviour.Popref_popi_pop1 => 3,
            _ => 0
        };
    }

    private static int EstimateStackPush(Instruction instr)
    {
        var opCode = instr.OpCode;

        // call/callvirt: 戻り値があれば1プッシュ
        if (opCode.Code is Code.Call or Code.Callvirt)
        {
            var sig = GetMethodSig(instr);
            if (sig == null) return 0;
            return sig.RetType != null && sig.RetType.ElementType != ElementType.Void ? 1 : 0;
        }

        // newobj は常に1プッシュ
        if (opCode.Code == Code.Newobj)
            return 1;

        return opCode.StackBehaviourPush switch
        {
            StackBehaviour.Push0 => 0,
            StackBehaviour.Push1 or StackBehaviour.Pushi or StackBehaviour.Pushi8
                or StackBehaviour.Pushr4 or StackBehaviour.Pushr8
                or StackBehaviour.Pushref => 1,
            StackBehaviour.Push1_push1 => 2,
            _ => 0
        };
    }

    /// <summary>
    /// IMethodDefOrRef と MethodSpec の両方からメソッドシグネチャを取得する。
    /// ジェネリックメソッド呼び出し (MethodSpec) にも対応。
    /// </summary>
    private static MethodSig? GetMethodSig(Instruction instr)
    {
        if (instr.Operand is IMethodDefOrRef methodRef)
            return methodRef.MethodSig;
        if (instr.Operand is MethodSpec methodSpec)
            return methodSpec.Method?.MethodSig;
        return null;
    }

    private static bool IsInjectedHelperType(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        return name.All(c => c is '\u200B' or '\u200C' or '\u200D' or '\uFEFF');
    }
}
