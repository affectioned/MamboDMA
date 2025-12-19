using System;

namespace MamboDMA.Games.CS2
{
    public static class CS2Offsets
    {
        // Module: client.dll
        // dwEntityList is located when cs:?Init @CUtlScratchMemoryPool creates memory pool
        // ; public: void CUtlScratchMemoryPool::Init(unsigned int, unsigned int, void *, bool)
        /*.text:00007FFBB1223AF2 call    cs:?Init @CUtlScratchMemoryPool@@QEAAXIIPEAX_N @Z; CUtlScratchMemoryPool::Init(uint, uint, void*, bool)
          .text:00007FFBB1223AF8 mov     rbx, cs:off_7FFBB2126B08
          .text:00007FFBB1223AFF lea     rax, [rsi+10h]
          .text:00007FFBB1223B03 xor     r15d, r15d
          .text:00007FFBB1223B06 mov     cs:qword_7FFBB1E81D78, rsi*/
        public static ulong dwEntityList;

        // ??_R4?$_Func_impl_no_alloc@V_lambda_1_@?1??ProcessUserInfoConvarChangesForNewController@CBasePlayerController
        // @@AEAAXXZ@_NP6AXPEAVConVarRefAbstract@@VCSplitScreenSlot@@PEBX2VConVarUserInfoSet_t@@PEAX@ZPEAX@std@@6B@
        // ; __int64 __fastcall sub_7FFBB0789D60(__int64, int, __int64, __int64, int, __int64 (*)(void))
        /*.text:00007FFBB0789D60 sub_7FFBB0789D60 proc near; DATA XREF: sub_7FFBB07BFE20+D↓o
        .text:00007FFBB0789D60
        .text:00007FFBB0789D60 arg_28 = qword ptr  30h
        .text:00007FFBB0789D60
        .text:00007FFBB0789D60 cmp     edx, 0FFFFFFFFh
        .text:00007FFBB0789D63 jz      short locret_7FFBB0789D80
        .text:00007FFBB0789D65 movsxd  rax, edx
        .text:00007FFBB0789D68 lea     rdx, qword_7FFBB1F8BC58
        .text:00007FFBB0789D6F mov     rdx, [rdx+rax*8]
        .text:00007FFBB0789D73 test    rdx, rdx
        .text:00007FFBB0789D76 jz      short locret_7FFBB0789D80
        .text:00007FFBB0789D78 mov     rax, [rsp+arg_28]
        .text:00007FFBB0789D7D jmp     rax*/
        public static ulong dwLocalPlayerController;


        // also close to C:\buildworker\csgo_rel_win64\build\src\game\client\view.cpp',
        // .rdata:00007FFBB16AC4B0                 dq offset sub_7FFBB043FA10
       /*.rdata:00007FFBB16AC4B8 dq offset ??_R4CRenderGameSystem@@6B @_0; const CRenderGameSystem::`RTTI Complete Object Locator'
         .rdata:00007FFBB16AC4C0 ; const CRenderGameSystem::`vftable'*/
        // ; char* __fastcall sub_7FFBB043FA10(__int64, int)
        //.text:00007FFBB043FA10 sub_7FFBB043FA10 proc near; DATA XREF: .rdata:00007FFBB16AC4B0↓o
        //.text:00007FFBB043FA10 movsxd  rax, edx
        //.text:00007FFBB043FA13 lea     rcx, unk_7FFBB1FA0450
        //.text:00007FFBB043FA1A shl     rax, 6
        //.text:00007FFBB043FA1E add     rax, rcx
        //.text:00007FFBB043FA21 retn
        //.text:00007FFBB043FA21 sub_7FFBB043FA10 endp
        public static ulong dwViewMatrix;

        // public static class CBasePlayerController 
        //.data:00007FFBB1D2D708                 dq offset aMHpawn       ; "m_hPawn"
        // .data:00007FFBB1D2D710 dw 6B4h
        public static ulong m_hPawn = 0x6B4; // CHandle<C_BasePlayerPawn>

        // public static class CBasePlayerController
        // .data:00007FFBB1D2DB18                 dq offset aMIszplayername ; "m_iszPlayerName"
        // .data:00007FFBB1D2DB20 dw 6E8h
        public const ulong m_iszPlayerName = 0x6E8; // char[128]
        /*.data:00007FFBB1D2DC50 dq offset aMBislocalplaye; "m_bIsLocalPlayerController"
          .data:00007FFBB1D2DC58 dw 778h*/
        public const ulong m_bIsLocalPlayerController = 0x778; // bool
        /*.data:00007FFBB1D2D6A0 dq offset aMSteamid; "m_steamID"
          .data:00007FFBB1D2D6A8 dw 770h*/
        public const ulong m_steamID = 0x770; // uint64

        // public static class C_BaseEntity
        /* .data:00007FFBB1CEEB80 dq offset aMLifestate; "m_lifeState"
           .data:00007FFBB1CEEB88 db  88h
           .data:00007FFBB1CEEB89 db  22h ; "
           .data:00007FFBB1CEEB8A db  5Ah ; Z
           .data:00007FFBB1CEEB8B db  5Dh ; ]
           .data:00007FFBB1CEEB8C db 0FCh
           .data:00007FFBB1CEEB8D db  7Fh ; 
           .data:00007FFBB1CEEB8E db    0
           .data:00007FFBB1CEEB8F db    0
           .data:00007FFBB1CEEB90 dw 354h*/
        public const ulong m_lifeState = 0x354; // uint8

        // public static class C_BaseEntity
        /*.data:00007FFBB1D48A08 dq offset aMIhealth; "m_iHealth"
        .data:00007FFBB1D48A10 db  4Ch ; L*/
        public const ulong m_iHealth = 0x34C; // int32

        // public static class C_BaseEntity
/*      .data:00007FFBB1CC2610 dq offset aMIteamnum; "m_iTeamNum"
        .data:00007FFBB1CC2618 dw 3EBh*/
        public const ulong m_iTeamNum = 0x3EB; // uint8

        // public static class C_BasePlayerPawn
/*      .data:00007FFBB1D4ECC0 E8 A4 6B B1 FB 7F 00 00                                         dq offset aMVoldorigin  ; "m_vOldOrigin"
        .data:00007FFBB1D4ECC8 A0 15                                                           dw 15A0h*/
        public const ulong m_vOldOrigin = 0x15A0; // Vector

        public static void ResolveOffsets(DmaMemory.ModuleInfo moduleInfo)
        {
            var dwEntityListPtr = DmaMemory.FindSignature(
                "48 89 35 ? ? ? ? 48 85 F6",
                moduleInfo.Base,
                moduleInfo.Base + moduleInfo.Size);

            if (dwEntityListPtr == 0)
                throw new Exception("dwEntityListPtr pattern not found");

            var dwEntityListPtrRva = DmaMemory.Read<int>(dwEntityListPtr + 3);

            // mov [rip+disp32], rsi → length 7, dispOffset 3
            /*48 89 35 ?? ?? ?? ??
            ^^^^^^^^^^^^^^^^
            |  |  | disp32(4 bytes)
            |  | ModRM
            | opcode
            REX*/
            dwEntityList = dwEntityListPtr.AddRVA(7, dwEntityListPtrRva);
            //dwEntityList = ResolveRipRelativeRva(dwEntityListPtr, 7, 3, moduleInfo.Base);

            var dwLocalPlayerControllerPtr = DmaMemory.FindSignature(
                "48 8D 15 ? ? ? ? ? ? ? ? 48 85 D2 74 ? 48 8B 44 24",
                moduleInfo.Base,
                moduleInfo.Base + moduleInfo.Size);

            if (dwLocalPlayerControllerPtr == 0)
                throw new Exception("dwLocalPlayerControllerPtr pattern not found");

            var dwLocalPlayerControllerPtrRva = DmaMemory.Read<int>(dwLocalPlayerControllerPtr + 3);

            // lea     rdx, qword_7FFBB1F8BC58
            // 48 8D 15 -> 1 + 1 + 1
            // ? ? ? ? ? ? ? ?
            dwLocalPlayerController = dwLocalPlayerControllerPtr.AddRVA(7, dwLocalPlayerControllerPtrRva);
            //dwLocalPlayerController = ResolveRipRelativeRva(dwLocalPlayerControllerPtr, 7, 3, moduleInfo.Base);

            var dwViewMatrixPtr = DmaMemory.FindSignature(
                "48 8D 0D ? ? ? ? 48 C1 E0 06",
                moduleInfo.Base,
                moduleInfo.Base + moduleInfo.Size);

            if (dwViewMatrixPtr == 0)
                throw new Exception("dwViewMatrixPtr pattern not found");

            var dwViewMatrixPtrRva = DmaMemory.Read<int>(dwViewMatrixPtr + 3);

            // lea     rcx, unk_7FFBB1FA0450
            dwViewMatrix = dwViewMatrixPtr.AddRVA(7, dwViewMatrixPtrRva);
            //dwViewMatrix = ResolveRipRelativeRva(dwViewMatrixPtr, 7, 3, moduleInfo.Base);
        }

/*        private static ulong ResolveRipRelativeRva(
            ulong instrAddress,
            int instrLength,
            int dispOffset,
            ulong moduleBase)
        {
            int disp32 = DmaMemory.Read<int>(instrAddress + (ulong)dispOffset);
            ulong next = instrAddress + (ulong)instrLength;
            ulong absAddr = (ulong)((long)next + disp32);
            return absAddr - moduleBase; // RVA
        }*/
    }
}
