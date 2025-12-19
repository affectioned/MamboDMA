using System.Runtime.Intrinsics.Arm;
using static System.Net.Mime.MediaTypeNames;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace MamboDMA.Games.DBD
{
    public static class DBDOffsets
    {
        // UWorld* World = GEngine->GetWorldFromContextObject(WorldContextObject, EGetWorldErrorMode::ReturnNull);
        /*.text:000000014460C4 76 48 8B 3D 93 48 39 07                                         mov rdi, cs:qword_14B9A0D10
        .text:000000014460C47D
        .text:000000014460C47D loc_14460C47D:                          ; CODE XREF: sub_14460C3B0+C4↑j
        .text:000000014460C47D 48 8B C7                                                        mov rax, rdi*/
        // sub_1413BA0C0(&v11, L"No world was found for object passed in to UEngine::GetWorldFromContextObject().");
        // sub_141751D40(L"A null object was passed as a world context object to UEngine::GetWorldFromContextObject().",a2,&v9);
        public static ulong GWorld;

        // we're using a logging function for this signature, not sure if it's the best, but we can have it for now
        // UE_LOG(LogTemp, Warning, TEXT("Player Health: %f"), CurrentHealth);
        // UE_LOG(LogTemp, Error, TEXT("Something went wrong in the function!"));
        /*.text:0000000140C859CE 48 8D 3D 2B EE A8 0A lea     rdi, unk_14B714800
          .text:0000000140C859D5 74 05                                               jz short loc_140C859DC
          .text:0000000140C859D7 48 8B C7                                            mov rax, rdi*/
/*      v25 = L"LogDevObjectVersion";
        v26 = v3 - L"LogDevObjectVersion";
        if (v26 && (v5 = sub_141537F80(L"LogDevObjectVersion", &v26, v4), v6 = (unsigned int)v26, v7 = v5, v26) )*/
        public static ulong GNames;

        // basically https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Runtime/CoreUObject/UObjectBaseInit
        // Final phase of UObject initialization. all auto register objects are added to the main data structures.
        // void UObjectBaseInit()
/*      .text:000000014178686B 48 89 05 0E 16 06 0A mov     cs:qword_14B7E7E80, rax
        .text:0000000141786872 E8 C7 C1 9F 06                                      call memset
        .text:0000000141786877 40 84 F6 test    sil, sil*/
        // sub_1413C6440(v21, "UObjectBaseInit");
        // (unsigned int)L"/Script/Engine.GarbageCollectionSettings",
        // (unsigned int)L"gc.MaxObjectsNotConsideredByGC",
        // v4 = L"Presizing";
        // v4 = L"Pre-allocating"; 
        public static ulong GObjects;



        public static void ResolveOffsets(DmaMemory.ModuleInfo moduleInfo)
        {
            var GWorldPtr = DmaMemory.FindSignature(
                "48 8B 3D ? ? ? ? 48 8B C7",
                moduleInfo.Base,
                moduleInfo.Base + moduleInfo.Size);

            if (GWorldPtr == 0)
                throw new Exception("GWorldPtr pattern not found");

            var GWorldPtrRva = DmaMemory.Read<int>(GWorldPtr + 3);

            GWorld = GWorldPtr.AddRVA(7, GWorldPtrRva);


            var GNamesPtr = DmaMemory.FindSignature(
                "48 8D 3D ? ? ? ? 74 05 48 8B C7",
                moduleInfo.Base,
                moduleInfo.Base + moduleInfo.Size);

            if (GNamesPtr == 0)
                throw new Exception("GNames pattern not found");

            var GNamesPtrRva = DmaMemory.Read<int>(GNamesPtr + 3);

            GNames = GNamesPtr.AddRVA(7, GNamesPtrRva);

            var GObjectsPtr = DmaMemory.FindSignature(
                "48 89 05 ? ? ? ? E8 ? ? ? ? 40 84 F6",
                moduleInfo.Base,
                moduleInfo.Base + moduleInfo.Size);

            if (GObjectsPtr == 0)
                throw new Exception("GObjects pattern not found");

            var GObjectsPtrRva = DmaMemory.Read<int>(GObjectsPtr + 3);

            GObjects = GObjectsPtr.AddRVA(7, GObjectsPtrRva);
        }
    }
}
