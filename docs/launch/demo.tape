# Records the contract-break and Skuld experiments from demo.md.
# Usage (repository root, clean working tree): vhs docs/launch/demo.tape
Output docs/launch/demo.gif

Set Shell "bash"
Set FontSize 16
Set Width 1400
Set Height 800
Set TypingSpeed 40ms
Set Theme "Catppuccin Mocha"

# Warm the build so the recording only shows the interesting output.
Hide
Type "dotnet build -v q --nologo > /dev/null && clear"
Enter
Wait
Show

# 1. A breaking contract change fails the build.
Type "# 1. Break a published contract: Amount decimal -> double"
Enter
Type "sed -i.bak 's/decimal Amount, string Currency/double Amount, string Currency/' samples/Shop/Contracts/IntegrationEvents.cs"
Enter
Type "dotnet build samples/Shop --nologo -v q 2>&1 | grep WYRD0201 | sort -u | cut -c1-200"
Enter
Wait+Screen@120s /WYRD0201/
Sleep 5s
Type "mv samples/Shop/Contracts/IntegrationEvents.cs.bak samples/Shop/Contracts/IntegrationEvents.cs"
Enter
Type "clear"
Enter

# 2. Skuld finds the bug in a fragile process.
Type "# 2. Remove every .Retry(...) from the order process, then ask Skuld"
Enter
Type "sed -i.bak -E 's/\.Retry\(5\.Times(, Backoff\.Exponential)?\)//g' samples/Shop/Fulfillment/OrderFulfillment.cs"
Enter
Type "dotnet test samples/Shop.Tests --filter the_business_holds_in_every_future 2>&1 | grep -E 'explored|First|Replay' | cut -c1-110"
Enter
Wait+Screen@180s /Replay/
Sleep 6s
Type "mv samples/Shop/Fulfillment/OrderFulfillment.cs.bak samples/Shop/Fulfillment/OrderFulfillment.cs"
Enter
Sleep 2s
