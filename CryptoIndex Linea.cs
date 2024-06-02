using Neo;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;
using System;
using System.Numerics;

namespace MemeIndex
{
  [DisplayName("MemeIndex")]
  [ManifestExtra("Author", "Your Name")]
  [ManifestExtra("Email", "your.email@example.com")]
  [ManifestExtra("Description", "A Meme Index Smart Contract for Neo")]
  public class MemeIndex : SmartContract
  {
    private const byte PrefixIndex = 0x01;
    private const byte PrefixUserIndex = 0x02;
    private const byte PrefixFeePercentage = 0x03;

    private const ulong DefaultFeePercentage = 10;

    public static event Action<UInt160, byte[], BigInteger[]> IndexCreated;
    public static event Action<UInt160, byte, BigInteger> IndexBought;
    public static event Action<UInt160, BigInteger> FeeCollected;
    public static event Action<UInt160, byte, BigInteger> IndexSold;

    public static void _deploy(object data, bool update)
    {
      if (!update)
      {
        Storage.Put(Storage.CurrentContext, new byte[] { PrefixFeePercentage }, DefaultFeePercentage);
      }
    }

    public static void CreateIndex(UInt160[] tokens, BigInteger[] percentages)
    {
      Assert(Runtime.CheckWitness(ContractManagement.GetContract(Runtime.ExecutingScriptHash).Owner), "Unauthorized");
      Assert(tokens.Length == percentages.Length && tokens.Length == 5, "Invalid parameters");
      BigInteger totalPercentage = 0;
      foreach (BigInteger percentage in percentages)
      {
        totalPercentage += percentage;
      }
      Assert(totalPercentage == 100, "Total percentage must be 100");

      byte indexId = (byte)Storage.Get(Storage.CurrentContext, "indexCount").ToBigInteger();
      Storage.Put(Storage.CurrentContext, new byte[] { PrefixIndex, indexId }, tokens);
      Storage.Put(Storage.CurrentContext, new byte[] { PrefixIndex, indexId, 0x01 }, percentages);
      Storage.Put(Storage.CurrentContext, "indexCount", indexId + 1);

      IndexCreated(Runtime.ExecutingScriptHash, tokens, percentages);
    }

    public static void BuyIndex(byte indexId)
    {
      UInt160 buyer = (UInt160)ExecutionEngine.CallingScriptHash;
      Assert(Storage.Get(Storage.CurrentContext, new byte[] { PrefixUserIndex, buyer, indexId }).Length == 0, "Index already bought");

      UInt160[] tokens = (UInt160[])StdLib.Deserialize(Storage.Get(Storage.CurrentContext, new byte[] { PrefixIndex, indexId }));
      BigInteger[] percentages = (BigInteger[])StdLib.Deserialize(Storage.Get(Storage.CurrentContext, new byte[] { PrefixIndex, indexId, 0x01 }));
      BigInteger totalCost = Runtime.ScriptContainer.GetReferences()[0].Value;

      BigInteger[] tokenAmounts = new BigInteger[tokens.Length];
      for (int i = 0; i < tokens.Length; i++)
      {
        tokenAmounts[i] = (totalCost * percentages[i]) / 100;
        NEP17.Transfer(buyer, ExecutionEngine.ExecutingScriptHash, tokenAmounts[i]);
      }

      UserIndex userIndex = new UserIndex { IndexId = indexId, BnbSpent = totalCost, TokenAmounts = tokenAmounts, Exists = true };
      Storage.Put(Storage.CurrentContext, new byte[] { PrefixUserIndex, buyer, indexId }, StdLib.Serialize(userIndex));

      IndexBought(buyer, indexId, totalCost);
    }

    public static void SellIndex(byte indexId, BigInteger bnbAmount)
    {
      UInt160 seller = (UInt160)ExecutionEngine.CallingScriptHash;
      UserIndex userIndex = (UserIndex)StdLib.Deserialize(Storage.Get(Storage.CurrentContext, new byte[] { PrefixUserIndex, seller, indexId }));
      Assert(userIndex.Exists, "No index found for user");


      UInt160[] tokens = (UInt160[])StdLib.Deserialize(Storage.Get(Storage.CurrentContext, new byte[] { PrefixIndex, indexId }));
      BigInteger[] percentages = (BigInteger[])StdLib.Deserialize(Storage.Get(Storage.CurrentContext, new byte[] { PrefixIndex, indexId, 0x01 }));

      BigInteger totalReturn = 0;
      BigInteger percentageToSell = (bnbAmount * 100) / userIndex.BnbSpent;
      Assert(percentageToSell <= 100, "Cannot sell more than the total index value");

      for (int i = 0; i < tokens.Length; i++)
      {
        BigInteger amount = (userIndex.TokenAmounts[i] * percentageToSell) / 100;
        NEP17.Transfer(ExecutionEngine.ExecutingScriptHash, seller, amount);
        totalReturn += amount;
      }

      BigInteger fee = (totalReturn * Storage.Get(Storage.CurrentContext, new byte[] { PrefixFeePercentage }).ToBigInteger()) / 100;
      BigInteger userAmount = totalReturn - fee;

      NEP17.Transfer(ExecutionEngine.ExecutingScriptHash, ContractManagement.GetContract(Runtime.ExecutingScriptHash).Owner, fee);
      NEP17.Transfer(ExecutionEngine.ExecutingScriptHash, seller, userAmount);

      userIndex.BnbSpent -= bnbAmount;
      for (int i = 0; i < userIndex.TokenAmounts.Length; i++)
      {
        userIndex.TokenAmounts[i] = (userIndex.TokenAmounts[i] * (100 - percentageToSell)) / 100;
      }

      if (userIndex.BnbSpent == 0)
      {
        Storage.Delete(Storage.CurrentContext, new byte[] { PrefixUserIndex, seller, indexId });
      }
      else
      {
        Storage.Put(Storage.CurrentContext, new byte[] { PrefixUserIndex, seller, indexId }, StdLib.Serialize(userIndex));
      }

      FeeCollected(ContractManagement.GetContract(Runtime.ExecutingScriptHash).Owner, fee);
      IndexSold(seller, indexId, userAmount);
    }

    public static void UpdateFeePercentage(BigInteger newFeePercentage)
    {
      Assert(Runtime.CheckWitness(ContractManagement.GetContract(Runtime.ExecutingScriptHash).Owner), "Unauthorized");
      Assert(newFeePercentage <= 100, "Fee percentage must be less than or equal to 100");
      Storage.Put(Storage.CurrentContext, new byte[] { PrefixFeePercentage }, newFeePercentage);
    }

    public static UserIndex[] GetUserIndexes(UInt160 user)
    {
      byte indexCount = (byte)Storage.Get(Storage.CurrentContext, "indexCount").ToBigInteger();
      UserIndex[] userIndexesArray = new UserIndex[indexCount];

      for (byte i = 0; i < indexCount; i++)
      {
        byte[] data = Storage.Get(Storage.CurrentContext, new byte[] { PrefixUserIndex, user, i });
        if (data.Length > 0)
        {
          userIndexesArray[i] = (UserIndex)StdLib.Deserialize(data);
        }
      }
      return userIndexesArray;
    }

    private struct UserIndex
    {
      public byte IndexId;
      public BigInteger BnbSpent;
      public BigInteger[] TokenAmounts;
      public bool Exists;
    }
  }
}