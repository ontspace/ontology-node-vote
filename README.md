# Ontology节点投票合约

合约说明
本合约的功能是合约发起人发起一本众筹ONT的投票合约，该合约会投票给指定ontology节点，发起人在合约中约定ONT在一轮分润周期内的利息，比如10000 ONT可获得1000 ONG

合约功能

本合约主要提供四个功能

### Vote
- 调用者 **投资者**
- 时间区间 **begin block之前**

个人投资者将自己的ONT投给本合约，相当于集资功能

### VoteToPeer
- 调用者 **合约的owner**
- 时间区间  **begin block之前**

合约的owner一次性将所有筹集的ONT投票给指定的节点

### Unvote
- 调用者 **任何人**

分两个阶段

- **Begin block 之后，end block 之前**

当调用votetopeer后，任何人都可以调用本方法，合约会向治理合约提取withdraw ONT的申请，需要等到该合约指定的end block高度后（分润的高度），总计的ONT会被退还到本合约

- **在end block之后调用**

个人投资者拿回自己的本息，举例：假如一个投资者投资10000个ONT，经过步骤一unvote之后，可以再次调用unvote，提取10000个ONT以及对应的ONG

### QuitNode

- 调用者 **合约的owner**
- 时间区间 **quitblock之后**

当所有的投资者都调用unvote收回自己本息之后，owner可以将合约中剩余的ONT和ONG都转移到指定的回收账户，确保合约中不会剩下任何的ONT和ONG

**注意：如果有的投资者没有调用步骤3的unvote，则会导致该投资者的ONT和ONG都会通过步骤4进入回收账户，步骤四是一个兜底的方法**



## 定制合约

- **admin**

  合约的管理员账户，只有该账户可以调用votetopeer, quitnode两个方法

- **totalCap**

  总计众筹ONT的额度，超过了不予接受

- **ongRate** 
   ONT对ONG分润比率（百分比）

- **beginBlock，endBlock，quitBlock**

  beginblock是治理合约某一轮分润结束的区块高度，endblock是新一轮分润的开始区块高度

  **投资者只能在beginblock之前调用vote功能，合约的owner也只能在beginblock之前调用votetopeer功能**

  任何人都可以在beginblock之后，endblock之前调用unvote，**且至少调用一次**，并且只能在endblock之后分别赎回自己的ONT和ONG

  合约的owner可以在quitblock之后调用quitnode，将合约中所有剩余的余额转移到指定钱包

- **recycleAddr**
  回收账户，调用quitnode后，回收所有ONT和ONG的账户，可以和admin是同一个账户

- **pubKey**
  投票节点的pubkey，它代表本合约投给哪一个节点
