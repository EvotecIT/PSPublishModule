Add-Type -TypeDefinition @'
using System;
using System.Collections;
public sealed class MethodPipelineValue {
    public static string Trace="";
    public int Count;
    public string Failure;
    public IEnumerable GetItems() {
        Trace+="method;";
        if(Failure=="method") throw new InvalidOperationException("method failed");
        return new Items(this);
    }
    private sealed class Items : IEnumerable {
        private readonly MethodPipelineValue owner;
        public Items(MethodPipelineValue value) { owner=value; }
        public IEnumerator GetEnumerator() {
            Trace+="get;";
            if(owner.Failure=="get") throw new InvalidOperationException("get failed");
            return new Cursor(owner);
        }
    }
    private sealed class Cursor : IEnumerator, IDisposable {
        private readonly MethodPipelineValue owner;
        private int index=-1;
        public Cursor(MethodPipelineValue value) { owner=value; }
        public bool MoveNext() {
            index++; Trace+="move"+index+";";
            if(owner.Failure=="move" && index==1) throw new InvalidOperationException("move failed");
            return index<owner.Count;
        }
        public object Current { get {
            Trace+="current"+index+";";
            if(owner.Failure=="current" && index==1) throw new InvalidOperationException("current failed");
            return index==0 ? (object)"first" : new object[] {null,"nested"};
        } }
        public void Reset() { throw new NotSupportedException(); }
        public void Dispose() { Trace+="dispose;"; if(owner.Failure=="dispose") throw new InvalidOperationException("dispose failed"); }
    }
}
'@
foreach($name in 'Read-MethodDirect','Read-MethodParenthesized','Read-MethodLiteral','Read-MethodReturn','Read-MethodUncaught','Read-MethodFinally') {
    foreach($case in 'empty','one','two','null','method','get','move','current','dispose') {
        $value=[MethodPipelineValue]::new(); $value.Count=2; $value.Failure=$case
        if($case -eq 'empty') {$value.Count=0}
        if($case -eq 'one') {$value.Count=1}
        if($case -eq 'null') {$value=$null}
        foreach($action in 'Continue','Stop') {
            [MethodPipelineValue]::Trace=''; $errors=@(); $warnings=@()
            $outer=$null; $records=@()
            try { $records=@(& $name -Value $value -ErrorAction $action -ErrorVariable errors -WarningAction SilentlyContinue -WarningVariable warnings 2>$null) }
            catch { $outer=$_.FullyQualifiedErrorId }
            [pscustomobject]@{name=$name;case=$case;action=$action;records=$records;trace=[MethodPipelineValue]::Trace;
                outer=$outer;errors=@($errors | ForEach-Object {$_.FullyQualifiedErrorId});warnings=@($warnings | ForEach-Object {$_.Message})} | ConvertTo-Json -Depth 9 -Compress
        }
    }
}

foreach($type in 'System.DayOfWeek','System.ConsoleColor','System.String','Missing.Type','') {
  $errors=@();$records=@(Get-ObjectEnumValues -enum $type 2>&1)
  $description=@($records | ForEach-Object {
    if($_ -is [Management.Automation.ErrorRecord]) { 'error:'+ $_.FullyQualifiedErrorId }
    elseif($_ -is [Collections.IDictionary]) {
      [pscustomobject]@{type=$_.GetType().FullName;pairs=@($_.GetEnumerator() | Sort-Object {[string]$_.Key} | ForEach-Object {$_.Key.GetType().FullName+':'+[string]$_.Key+':'+[string]$_.Value})}
    } else {[string]$_}
  })
  [pscustomobject]@{name='Get-ObjectEnumValues';input=$type;records=$description} | ConvertTo-Json -Depth 9 -Compress
}
