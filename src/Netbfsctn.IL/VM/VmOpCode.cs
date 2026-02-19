namespace Netbfsctn.IL.VM;

public enum VmOpCode : byte
{
    // 算術
    ADD = 0x01,
    SUB = 0x02,
    MUL = 0x03,
    DIV = 0x04,
    REM = 0x05,
    NEG = 0x06,

    // 比較
    CEQ = 0x10,
    CLT = 0x11,
    CGT = 0x12,

    // スタック
    LDC_I4 = 0x20,
    LDC_I8 = 0x21,
    LDC_R8 = 0x22,
    LDSTR = 0x23,
    LDNULL = 0x24,
    DUP = 0x25,
    POP = 0x26,

    // ローカル/引数
    LDLOC = 0x30,
    STLOC = 0x31,
    LDARG = 0x32,
    STARG = 0x33,

    // 分岐
    BR = 0x40,
    BRTRUE = 0x41,
    BRFALSE = 0x42,

    // 呼び出し
    CALL = 0x50,

    // オブジェクト
    NEWOBJ = 0x60,
    LDFLD = 0x61,
    STFLD = 0x62,
    LDSFLD = 0x63,
    STSFLD = 0x64,

    // 変換
    CONV_I4 = 0x70,
    CONV_I8 = 0x71,
    CONV_R8 = 0x72,
    BOX = 0x73,
    UNBOX = 0x74,

    // 配列
    NEWARR = 0x80,
    LDELEM = 0x81,
    STELEM = 0x82,
    LDLEN = 0x83,

    // 制御
    RET = 0xF0,
    THROW = 0xF1,

    // No-op
    NOP = 0xFF
}
