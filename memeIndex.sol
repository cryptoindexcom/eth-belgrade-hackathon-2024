// SPDX-License-Identifier: MIT
pragma solidity ^0.8.18;

import {IERC20} from "@chainlink/contracts-ccip/src/v0.8/vendor/openzeppelin-solidity/v4.8.3/contracts/token/ERC20/IERC20.sol";
import {SafeERC20} from "@chainlink/contracts-ccip/src/v0.8/vendor/openzeppelin-solidity/v4.8.3/contracts/token/ERC20/utils/SafeERC20.sol";
import {OwnerIsCreator} from "@chainlink/contracts-ccip/src/v0.8/shared/access/OwnerIsCreator.sol";

import "@openzeppelin/contracts/utils/Address.sol";

interface IPancakeRouter {
    function WETH() external pure returns (address);

    function getAmountsOut(uint amountIn, address[] calldata path) external view returns (uint[] memory amounts);
    function swapExactETHForTokens(uint amountOutMin, address[] calldata path, address to, uint deadline) external payable returns (uint[] memory amounts);
    function swapExactTokensForETH(uint amountIn, uint amountOutMin, address[] calldata path, address to, uint deadline) external returns (uint[] memory amounts);
}
// ["0x78867BbEeF44f2326bF8DDd1941a4439382EF2A7"]
contract MemeIndex is OwnerIsCreator {
    using SafeERC20 for IERC20;
    using Address for address;

    IPancakeRouter public immutable pancakeRouter; //0x9Ac64Cc6e4415144C455BD8E4837Fea55603e5c3
    uint256 public feePercentage = 10; // Fee percentage (10%)

    struct Index {
        address[] tokens;
        uint256[] percentages;
        bool exists;
    }

    mapping(uint256 => Index) public indices;
    uint256 public indexCount;

    struct UserIndex {
        uint256 indexId;
        uint256 bnbSpent;
        uint256[] tokenAmounts;
        bool exists;
    }

    mapping(address => mapping(uint256 => UserIndex)) public userIndexes;


    // mapping(address => UserIndex) public userIndexes;

    event IndexCreated(uint256 indexed indexId, address[] tokens, uint256[] percentages);
    event IndexBought(address indexed user, uint256 indexed indexId, uint256 totalCost);
    event IndexSold(address indexed user, uint256 totalReturn);
    event FeeCollected(address indexed owner, uint256 fee);
    event IndexSold(address indexed user, uint256 indexed indexId, uint256 totalReturn);

    constructor(address _pancakeRouter) {
        pancakeRouter = IPancakeRouter(_pancakeRouter);
    }

    function createIndex(address[] calldata tokens, uint256[] calldata percentages) external onlyOwner {
        require(tokens.length == percentages.length, "Tokens and percentages length mismatch");
        require(tokens.length == 5, "There must be exactly 5 tokens");
        uint256 totalPercentage = 0;
        for (uint256 i = 0; i < percentages.length; i++) {
            totalPercentage += percentages[i];
        }
        require(totalPercentage == 100, "Total percentage must be 100");

        indices[indexCount] = Index({tokens: tokens, percentages: percentages, exists: true});
        emit IndexCreated(indexCount, tokens, percentages);
        indexCount++;
    }

    function buyIndex(uint256 indexId) external payable  {
        Index storage index = indices[indexId];
        require(index.exists, "Index does not exist");
        uint256 totalCost = msg.value;
        uint256[] memory tokenAmounts = new uint256[](index.tokens.length);
        for (uint256 i = 0; i < index.tokens.length; i++) {
            uint256 tokenCost = (totalCost * index.percentages[i]) / 100;
            address[] memory path = new address[](2);
            path[0] = pancakeRouter.WETH();
            path[1] = index.tokens[i];
            uint256[] memory amountsOut;

            amountsOut = pancakeRouter.swapExactETHForTokens{value: tokenCost}(0, path, address(this), block.timestamp);
            tokenAmounts[i] = amountsOut[1];
        }

        userIndexes[msg.sender][indexId] = UserIndex({
            indexId: indexId,
            bnbSpent: totalCost,
            tokenAmounts: tokenAmounts,
            exists: true
        });


        emit IndexBought(msg.sender, indexId, totalCost);
    }
  function sellIndex(uint256 indexId, uint256 bnbAmount) external  {
        UserIndex storage userIndex = userIndexes[msg.sender][indexId];
        require(userIndex.exists, "No index found for user");

        uint256 totalReturn = 0;
        Index storage index = indices[indexId];

        uint256 percentageToSell = (bnbAmount * 1e18) / userIndex.bnbSpent; // scaled to 1e18 for precision
        require(percentageToSell <= 1e18, "Cannot sell more than the total index value");

        for (uint256 i = 0; i < userIndex.tokenAmounts.length; i++) {
            uint256 amount = (userIndex.tokenAmounts[i] * percentageToSell) / 1e18;
            IERC20(index.tokens[i]).approve(address(pancakeRouter), amount);

            address[] memory path = new address[](2);
            path[0] = index.tokens[i];
            path[1] = pancakeRouter.WETH();

            uint256[] memory amountsOut = pancakeRouter.swapExactTokensForETH(amount, 1, path, address(this), block.timestamp);
            totalReturn += amountsOut[1];
        }

        uint256 fee = (totalReturn * feePercentage) / 100;
        uint256 userAmount = totalReturn - fee;

        Address.sendValue(payable(owner()), fee);
        Address.sendValue(payable(msg.sender), userAmount);

        // Update the user's index to reflect the sale
        userIndex.bnbSpent -= bnbAmount;
        for (uint256 i = 0; i < userIndex.tokenAmounts.length; i++) {
            userIndex.tokenAmounts[i] = (userIndex.tokenAmounts[i] * (1e18 - percentageToSell)) / 1e18;
        }

        // If all tokens are sold, delete the user's index
        if (userIndex.bnbSpent == 0) {
            delete userIndexes[msg.sender][indexId];
        }

        emit FeeCollected(owner(), fee);
        emit IndexSold(msg.sender, indexId, userAmount);
    }

    function updateFeePercentage(uint256 newFeePercentage) external onlyOwner {
        require(newFeePercentage <= 100, "Fee percentage must be less than or equal to 100");
        feePercentage = newFeePercentage;
    }


    function getUserIndexes(address user) external view returns (UserIndex[] memory) {
        uint256 count = indexCount;
        UserIndex[] memory userIndexesArray = new UserIndex[](count);

        for (uint256 i = 0; i < count; i++) {
            if (userIndexes[user][i].exists) {
                userIndexesArray[i] = userIndexes[user][i];
            }
        }
        return userIndexesArray;
    }

    receive() external payable {}

    fallback() external payable {}
}
