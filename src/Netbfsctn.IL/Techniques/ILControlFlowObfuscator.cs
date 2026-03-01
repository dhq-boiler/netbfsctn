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
        foreach (var type in module.Types)
        {
            if (type.Name == "<Module>")
                continue;

            foreach (var method in type.Methods)
            {
                if (!method.HasBody) continue;
                if (method.Body.Instructions.Count < 4) continue;
                if (method.Body.ExceptionHandlers.Count > 0) continue;
                // switch 命令を含むメソッドはスキップ（ステートマシン変換と衝突する）
                if (method.Body.Instructions.Any(i => i.OpCode == OpCodes.Switch)) continue;

                try
                {
                    TransformToStatesMachine(method, module, context);
                    result.ObfuscatedMethods++;
                }
                catch (Exception ex)
                {
                    context.Logger.Verbose($"制御フロー変換をスキップ: {method.FullName} ({ex.Message})");
                }
            }
        }
    }

    private void TransformToStatesMachine(MethodDef method, ModuleDef module, ObfuscationContext context)
    {
        var body = method.Body;

        // 基本ブロックに分割
        var blocks = SplitIntoBasicBlocks(body);
        if (blocks.Count < 2)
            return;

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

        // ret (デフォルト - 到達しないはず)
        var exitLabel = new Instruction(OpCodes.Ret);
        newInstructions.Add(new Instruction(OpCodes.Br, exitLabel));

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

        // 命令を置換
        body.Instructions.Clear();
        foreach (var instr in newInstructions)
        {
            body.Instructions.Add(instr);
        }
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
}
