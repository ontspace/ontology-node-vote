using Ont.SmartContract.Framework;
using Ont.SmartContract.Framework.Services.Ont;
using Ont.SmartContract.Framework.Services.System;
using System;
using System.Numerics;

namespace ONTOLOGY_VOTE
{
    public class Contract1 : SmartContract
    {
        public static readonly byte[] ontAddr = "AFmseVrdL9f9oyCzZefL9tG6UbvhUMqNMV".ToScriptHash();
        public static readonly byte[] ongAddr = "AFmseVrdL9f9oyCzZefL9tG6UbvhfRZMHJ".ToScriptHash();
        public static readonly byte[] govAddr = "AFmseVrdL9f9oyCzZefL9tG6UbviEH9ugK".ToScriptHash();

        public static readonly byte[] admin = "AMAx993nE6NEqZjwBssUfopxnnvTdob9ij".ToScriptHash();
        public static readonly byte[] recycleAddr = "AH2Y9ngDT7m6KAQfNpr7H4BqVCdJJSttmV".ToScriptHash();
        public static readonly string pubKey = "02890c587f4e4a6a98b455248eabac04b733580cfe5f11acd648c675543dfbb926";

        private const ulong factor = 1000000000; // ONG Decimal
        private const ulong totalCap = 1000000; // 100W
        private const ulong ongRate = 5; // number of ONG returned per 100 ONT in one round

        private const ulong beginBlock = 8650;
        private const ulong endBlock = 8750;
        private const ulong quitBlock = 8800;


        public static Object Main(string operation, params object[] args)
        {
            if (operation == "Vote")
            {
                if (args.Length != 2) return false;
                byte[] from = (byte[])args[0];
                ulong value = (ulong)args[1];
                return Vote(from, value);
            }

            if (operation == "VoteToPeer")
            {
                if (args.Length != 1) return false;
                string pubKey = (string)args[0];
                return VoteToPeer(pubKey);
            }

            if (operation == "Unvote")
            {
                if (args.Length != 2) return false;
                byte[] from = (byte[])args[0];
                ulong value = (ulong)args[1];
                return Unvote(from, value);
            }

            if (operation == "QuitNode")
            {
                return QuitNode();
            }

            return false;
        }

        public static bool Vote(byte[] from, ulong value)
        {
            if (value <= 0) return false;
            if (!Runtime.CheckWitness(from)) return false;
            uint height = Blockchain.GetHeight();
            if (height >= beginBlock)
            {
                Runtime.Log("current block height greater than begin block height, can't vote.");
                return false;
            }

            byte[] voteContract = ExecutionEngine.ExecutingScriptHash;

            StorageContext context = Storage.CurrentContext;
            ulong totalAmount = (ulong)Storage.Get(context, "totalAmount").AsBigInteger();
            if (totalAmount < 0) return false;

            string flag = Storage.Get(context, "votepeerflag").AsString();
            if (flag == "true")
            {
                Runtime.Log("vote to peer has been finished.");
                return false;
            }
            ulong balanceCap = totalCap - totalAmount;
            if (value > balanceCap) value = balanceCap;

            byte[] ret = Native.Invoke(0, ontAddr, "transfer", new object[1] { new Transfer { From = from, To = voteContract, Value = value } });
            if (ret[0] != 1) return false;

            BigInteger balance = Storage.Get(context, from).AsBigInteger();
            Storage.Put(context, from, balance + value);
            Storage.Put(context, "totalAmount", totalAmount + value);
            return true;
        }

        public static bool Unvote(byte[] from, ulong value)
        {
            byte[] voteContract = ExecutionEngine.ExecutingScriptHash;
            byte[] ret;
            uint height = Blockchain.GetHeight();
            if (height < beginBlock)
            {
                Runtime.Log("can't unvote before beign block height");
                return false;
            }
            StorageContext context = Storage.CurrentContext;
            ulong voteValue = (ulong)Storage.Get(context, from).AsBigInteger();
            if (voteValue < value)
            {
                Runtime.Log("withdraw value more than voted value");
                return false;
            }

            ulong totalAmount = (ulong)Storage.Get(context, "totalAmount").AsBigInteger();
            if (totalAmount < 0) return false;

            string flag = Storage.Get(context, "unvotepeerflag").AsString();
            Runtime.Notify(flag);
            if (flag == "false")
            {
                Peer peer = new Peer { From = voteContract, Key = new string[] { pubKey }, Value = new ulong[] { totalAmount } };

                ret = Native.Invoke(0, govAddr, "unVoteForPeer", peer);
                if (ret[0] != 1)
                {
                    Runtime.Log("unvote peer failed");
                    return false;
                }
                Storage.Put(context, "unvotepeerflag", "true");
                Runtime.Log("unvote success, need to waiting ending block height.");
                return true;
            }

            if (height < endBlock)
            {
                Runtime.Log("need to wait ending block to withdraw.");
                return false;
            }

            flag = Storage.Get(context, "withdrawflag").AsString();
            Runtime.Log(flag);

            // withdraw ONT from vote peer node
            if (flag == "false")
            {
                ret = Native.Invoke(0, govAddr, "withdraw", new Withdraw { Account = voteContract, PeerPubkey = new string[] { pubKey }, Value = new ulong[] { totalAmount } });
                if (ret[0] != 1)
                {
                    Runtime.Log("withdraw ont failed");
                    return false;
                }
                Runtime.Log("withdraw ont success");
                Storage.Put(context, "withdrawflag", "true");
            }


            // reback ONT to voter
            object[] p = new object[1];
            Transfer t = new Transfer { From = voteContract, To = from, Value = value };
            p[0] = t;
            ret = Native.Invoke(0, ontAddr, "transfer", new object[1] { new Transfer { From = voteContract, To = from, Value = value } });
            if (ret[0] != 1)
            {
                return false;
            }

            // reback ONG to voter
            ret = Native.Invoke(0, ongAddr, "transfer", new object[1] { new Transfer { From = voteContract, To = from, Value = (value * ongRate * factor) / 100 } });
            if (ret[0] != 1) return false;

            Storage.Put(context, from, voteValue - value);
            return true;
        }

        public static bool VoteToPeer(string pubKey)
        {
            if (!Runtime.CheckWitness(admin)) return false;

            byte[] voteContract = ExecutionEngine.ExecutingScriptHash;

            uint height = Blockchain.GetHeight();
            if (height >= beginBlock)
            {
                Runtime.Log("current blockheight greater than beign block height");
                return false;
            }

            StorageContext context = Storage.CurrentContext;

            ulong totalOnt = (ulong)Storage.Get(context, "totalAmount").AsBigInteger();
            if (totalOnt < 0) return false;

            byte[] ret = Native.Invoke(0, ontAddr, "approve", new Approve { From = voteContract, To = govAddr, Value = (ulong)totalOnt });
            if (ret[0] != 1)
            {
                Runtime.Log("VoteToPeer approve ont failed.");
                return false;
            }

            ret = Native.Invoke(0, govAddr, "voteForPeerTransferFrom", new Peer { From = voteContract, Key = new string[] { pubKey }, Value = new ulong[] { (ulong)totalOnt } });
            if (ret[0] != 1)
            {
                Runtime.Log("votepeer failed");
                return false;
            }
            Runtime.Log("votepeer success");
            Storage.Put(context, "votepeerflag", "true");
            Storage.Put(context, "unvotepeerflag", "false");
            Storage.Put(context, "withdrawflag", "false");
            return true;
        }

        public static bool QuitNode()
        {
            if (!Runtime.CheckWitness(admin)) return false;
            byte[] voteContract = ExecutionEngine.ExecutingScriptHash;

            uint height = Blockchain.GetHeight();
            if (height < quitBlock)
            {
                Runtime.Log("current blockheight less than quit block height");
                return false;
            }

            StorageContext context = Storage.CurrentContext;
            Balance param = new Balance { Account = voteContract };
            ulong remainOnt = (ulong)Native.Invoke(0, ontAddr, "balanceOf", param).AsBigInteger();
            ulong remainOng = (ulong)Native.Invoke(0, ongAddr, "balanceOf", param).AsBigInteger();

            return RecycleAsset(voteContract, recycleAddr, remainOnt, remainOng);
        }

        private static bool RecycleAsset(byte[] from, byte[] to, ulong ont, ulong ong)
        {
            byte[] ret = Native.Invoke(0, ontAddr, "transfer", new object[1] { new Transfer { From = from, To = to, Value = ont } });
            if (ret[0] != 1) throw new Exception("recycle ont failed.");

            ret = Native.Invoke(0, ongAddr, "transfer", new object[1] { new Transfer { From = from, To = to, Value = ong } });
            if (ret[0] != 1) throw new Exception("recycle ong failed.");
            return true;
        }

        struct Transfer
        {
            public byte[] From;
            public byte[] To;
            public ulong Value;
        }

        struct Withdraw
        {
            public byte[] Account;
            public string[] PeerPubkey;
            public ulong[] Value;
        }

        struct Peer
        {
            public byte[] From;
            public string[] Key;
            public ulong[] Value;
        }

        struct Balance
        {
            public byte[] Account;
        }

        struct Approve
        {
            public byte[] From;
            public byte[] To;
            public ulong Value;
        }
    }
}
