"""Validate a fresh Unity NUnit report; a process exit code alone is insufficient."""
import argparse
import json
from pathlib import Path
import xml.etree.ElementTree as ET


def summarize(report):
    report = Path(report)
    summary = {"success": False, "report": str(report.resolve())}
    try:
        root = ET.parse(report).getroot()
        if root.tag != "test-run":
            raise ValueError("Expected NUnit test-run root")
        counts = {key: int(root.attrib[key]) for key in
                  ("total", "passed", "failed", "skipped", "inconclusive")}
        cases = list(root.iter("test-case"))
        consistent = (all(value >= 0 for value in counts.values())
                      and counts["total"] == sum(counts[k] for k in counts if k != "total")
                      and len(cases) == counts["total"])
        # Suite setup/teardown errors must fail even if individual cases passed.
        unsuccessful = [node.get("fullname", node.get("name", node.tag))
                        for node in root.iter()
                        if node.tag in ("test-suite", "test-case")
                        and node.get("result") != "Passed"]
        summary.update(counts, result=root.get("result"), consistent=consistent,
                       unsuccessful=unsuccessful)
        summary["success"] = (consistent and root.get("result") == "Passed"
                              and counts["total"] > 0
                              and counts["passed"] == counts["total"]
                              and not unsuccessful)
    except (OSError, ET.ParseError, ValueError, KeyError) as error:
        summary.update(reason="missing-or-invalid-report", message=str(error))
    return summary


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("report")
    args = parser.parse_args()
    result = summarize(args.report)
    print(json.dumps(result, ensure_ascii=False, indent=2))
    raise SystemExit(0 if result["success"] else 1)
