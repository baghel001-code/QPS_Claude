using I = MudBlazor.Icons.Material.Outlined;

namespace Admin.Components.Dashboard;

/// <summary>
/// Dummy dashboards with SAMPLE DATA, one per role (QpsRoles) plus Vendor and "no role".
/// Every page built from these shows a "Prototype · sample data" badge.
/// </summary>
public static class DashboardCatalog
{
    private static readonly string[] V =
        ["Shree Balaji Garments", "Kaveri Knitwear", "Om Sai Textiles", "Sunrise Home Linen", "Delta Footwear", "Metro Denim Co."];

    private static DashSide Pipeline(int received, int ongoing, int pending, int report, int cancelled) =>
        new("Inspection requests by status", "/AllRecPenOngInspections", SideKind.Bars,
            [new("Received", Value: received), new("Ongoing", Value: ongoing), new("Pending", Value: pending),
             new("Report", Value: report), new("Cancelled", Value: cancelled)],
            Hint: "This month, all DCs. Counts, not percentages.");

    private static SideItem Item(string label, string detail, string? status = null) => new(label, detail, status);
    private static SideItem Bar(string label, double value) => new(label, Value: value);

    public static readonly DashboardDefinition Administrator = new(
        "Administrator dashboard", "Users, roles and system health.",
        [new("User management", "/User-Master", I.ManageAccounts), new("User roles", "/Role-Master", I.Settings, false)],
        [new("Active users", "186", "Employees and vendors", "blue", I.People), new("Registered vendors", "312", "14 added this month", "green", I.Storefront),
         new("Access requests", "4", "Waiting for approval", "pink", I.Inbox), new("Locked accounts", "2", "Too many failed sign-ins", "red", I.Lock)],
        new("Recent account changes", "/User-Master", ["When", "User", "Type", "Change", "Status"],
        [["10:12", "Neha Gupta", "Employee", "Role QA (1006) → QAM (1007)", "Done"], ["09:40", V[5], "Vendor", "New vendor account", "Awaiting approval"],
         ["09:05", "Rahul Kumar", "Employee", "Locked after 5 failed sign-ins", "Locked"], ["Yesterday", V[1], "Vendor", "Password reset", "Done"]]),
        new("System status", null, SideKind.List,
            [Item("SAP PO sync", "Last run 10 min ago", "Done"), Item("Daily report e-mails", "Next run 18:00", "Scheduled"), Item("HRP employee login API", "Response 240 ms", "Active")]));

    public static readonly DashboardDefinition Admin = new(
        "Admin dashboard", "PPS requests and vendor onboarding.",
        [new("PPS requests", "/pps-list", I.Checkroom)],
        [new("PPS requests, this month", "58", "41 approved", "blue", I.Checkroom), new("Vendors onboarded", "14", "This month", "green", I.Storefront),
         new("Vendor password resets", "6", "This week", "amber", I.Lock)],
        new("Latest PPS requests", "/pps-list", ["Style", "Vendor", "Category", "Submitted", "Status"],
        [["TW-24-0102", V[3], "Towels", "28 Sep", "Awaiting approval"], ["WJ-24-0417", V[5], "Women's jeans", "27 Sep", "Awaiting approval"],
         ["BS-24-0288", V[2], "Bedsheets", "27 Sep", "Rework requested"], ["SH-24-0501", "Prime Leather Goods", "Belts", "24 Sep", "Approved"]]),
        new("Vendor onboarding", null, SideKind.List,
            [Item(V[5], "Documents received", "In progress"), Item("Nova Kids Wear", "Audit scheduled", "Pending"), Item("Prime Leather Goods", "Account created", "Done")]));

    public static readonly DashboardDefinition QAAdmin = new(
        "QA admin dashboard", "The whole inspection pipeline, vendor audits and master data at a glance.",
        [new("Bulk download", "/bulk-download", I.Download), new("New vendor audits", "/NewAuditRequests", I.Assignment, false)],
        [new("Inspection requests, this month", "486", "34 more than last month", "blue", I.FactCheck), new("Pass rate, 30 days", "91%", "Up 3 points", "green", I.TrendingUp),
         new("Vendor audits waiting", "12", "8 new · 4 existing", "pink", I.Assignment), new("Cancelled this month", "9", "2 by vendors", "red", I.ReportProblem)],
        new("Vendor audit requests", "/NewAuditRequests", ["Request", "Vendor", "Type", "Raised", "Status"],
        [["AR-1182", V[5], "New vendor", "26 Sep", "Awaiting review"], ["AR-1179", "Nova Kids Wear", "New vendor", "25 Sep", "Awaiting review"],
         ["AR-1174", V[2], "Re-audit", "24 Sep", "In progress"], ["AR-1170", V[4], "Re-audit", "22 Sep", "Approved"], ["AR-1166", "Prime Leather Goods", "New vendor", "20 Sep", "Rejected"]]),
        Pipeline(142, 61, 38, 229, 16));

    public static readonly DashboardDefinition QAM = new(
        "QA manager dashboard", "Received requests to assign, inspector workload and pending reports.",
        [new("Received requests", "/InspectionList", I.FactCheck)],
        [new("Received, not assigned", "9", "4 needed within 48 h", "pink", I.Inbox), new("Ongoing inspections", "14", "Across 3 DCs", "blue", I.PlayCircle),
         new("Pending reports", "6", "Oldest: 3 days", "amber", I.Schedule), new("Pass rate, 30 days", "91%", "Up 3 points", "green", I.TrendingUp)],
        new("Received requests waiting for an inspector", "/InspectionList", ["Request", "Vendor", "Category", "Qty", "Needed by", "Status"],
        [["IR-24811", V[5], "Women's jeans", "4,800", "01 Oct", "Received"], ["IR-24807", V[1], "Kids' T-shirts", "12,000", "01 Oct", "Received"],
         ["IR-24802", V[3], "Towels", "6,500", "02 Oct", "Received"], ["IR-24796", V[4], "Men's sandals", "3,200", "03 Oct", "Received"], ["IR-24790", V[2], "Bedsheets", "9,000", "04 Oct", "Received"]]),
        new("Inspector workload this week", null, SideKind.Bars,
            [Bar("Priya R.", 9), Bar("Vikas M.", 8), Bar("Rahul K.", 7), Bar("Neha S.", 6), Bar("Sana A.", 4), Bar("Rohit P.", 3)],
            Hint: "Inspections booked per inspector (plan: 10).", Max: 10));

    public static readonly DashboardDefinition QA = new(
        "QA dashboard", "Your inspections today and the reports still to submit.",
        [new("Ongoing inspections", "/OngoingInspections", I.PlayCircle)],
        [new("Today's inspections", "6", "2 before noon", "blue", I.CalendarMonth), new("Ongoing", "1", V[0], "amber", I.PlayCircle),
         new("Pending reports", "3", "Oldest: 2 days", "pink", I.Description), new("Failed this week", "2", "Of 21 inspected", "red", I.ReportProblem)],
        new("Today's schedule", "/OngoingInspections", ["Time", "Vendor", "PO", "Category", "Location", "Status"],
        [["09:30", V[0], "4500129012", "Men's shirts", "Gurugram DC", "Ongoing"], ["11:00", V[3], "4500129044", "Towels", "Gurugram DC", "Scheduled"],
         ["12:30", V[1], "4500129051", "Kids' T-shirts", "Tiruppur factory", "Scheduled"], ["14:00", V[2], "4500129077", "Bedsheets", "Bhiwandi DC", "Scheduled"],
         ["15:30", V[4], "4500129090", "Men's sandals", "Gurugram DC", "Scheduled"]]),
        new("Reports waiting for you", "/PendingInspectionListNew", SideKind.List,
            [Item(V[1], "PO 4500128841 · Kids' T-shirts", "Overdue"), Item(V[2], "PO 4500128790 · Bedsheets", "Due today"), Item(V[4], "PO 4500128702 · Men's sandals", "Due tomorrow")]));

    public static readonly DashboardDefinition CategoryHead = new(
        "Category head dashboard", "Quality across your category: pass rates, failures and cancellations.",
        [new("Inspection reports", "/InspectionsReportNew", I.Description)],
        [new("Inspections, 30 days", "212", "Your category", "blue", I.FactCheck), new("Pass rate, 30 days", "88%", "Target 90%", "amber", I.TrendingUp),
         new("Failed inspections", "14", "6 vendors", "red", I.ReportProblem), new("Cancelled", "5", "3 by vendors", "pink", I.Inbox)],
        new("Failed and cancelled inspections", "/InspectionsReportNew", ["Request", "Vendor", "Category", "Date", "Status"],
        [["IR-24612", "Nova Kids Wear", "Girls' dresses", "18 Sep", "Failed"], ["IR-24598", V[4], "Men's sandals", "17 Sep", "Failed"],
         ["IR-24577", V[5], "Women's jeans", "15 Sep", "Cancelled"], ["IR-24561", V[1], "Kids' shorts", "14 Sep", "Failed"]]),
        new("Pass rate by sub-category", null, SideKind.Bars,
            [Bar("Men's shirts", 94), Bar("Bedsheets", 92), Bar("Kids' T-shirts", 89), Bar("Women's jeans", 84), Bar("Men's sandals", 79)],
            Hint: "Last 30 days, % of inspections passed.", Max: 100, Suffix: "%"));

    public static readonly DashboardDefinition AssociateCategoryHead = new(
        "Associate category head dashboard", "Inspections and PPS for the sub-categories you look after.",
        [new("Pending inspections", "/PendingInspectionListNew", I.Schedule)],
        [new("Inspections, 30 days", "96", "Your sub-categories", "blue", I.FactCheck), new("Pass rate, 30 days", "90%", "On target", "green", I.TrendingUp),
         new("Failed inspections", "6", "3 vendors", "red", I.ReportProblem), new("Pending inspections", "8", "Next: 01 Oct", "amber", I.Schedule)],
        new("Pending inspections", "/PendingInspectionListNew", ["Request", "Vendor", "Category", "Date", "Status"],
        [["IR-24807", V[1], "Kids' T-shirts", "01 Oct", "Pending"], ["IR-24802", V[3], "Towels", "02 Oct", "Pending"], ["IR-24796", V[4], "Men's sandals", "03 Oct", "Pending"]]),
        new("Pass rate by sub-category", null, SideKind.Bars,
            [Bar("Kids' T-shirts", 93), Bar("Towels", 91), Bar("Men's sandals", 82)], Hint: "Last 30 days, % of inspections passed.", Max: 100, Suffix: "%"));

    public static readonly DashboardDefinition Merchandiser = new(
        "Merchandiser dashboard", "Inspections and samples for the styles you handle.",
        [new("All inspection requests", "/AllRecPenOngInspections", I.FactCheck), new("PPS requests", "/pps-list", I.Checkroom, false)],
        [new("My open inspection requests", "23", "5 received today", "blue", I.FactCheck), new("PPS waiting for decision", "4", "1 over 3 days", "pink", I.Checkroom),
         new("Failed, 30 days", "3", "Re-inspections booked", "red", I.ReportProblem), new("ASNs generated", "11", "This week", "green", I.LocalShipping)],
        new("Inspections for your styles", "/AllRecPenOngInspections", ["Style", "Vendor", "PO", "Inspection", "Status"],
        [["MS-24-1182", V[0], "4500129012", "30 Sep", "Ongoing"], ["WJ-24-0417", V[5], "4500129102", "30 Sep", "Received"],
         ["KT-24-0931", V[1], "4500129051", "01 Oct", "Pending"], ["BS-24-0288", V[2], "4500128790", "27 Sep", "Passed"], ["GD-24-0112", "Nova Kids Wear", "4500128702", "24 Sep", "Failed"]]),
        Pipeline(18, 6, 5, 41, 2));

    public static readonly DashboardDefinition Buyer = new(
        "Buyer dashboard", "Pre-production samples to approve, inspections on your POs and ASNs.",
        [new("Review PPS requests", "/pps-list", I.Checkroom)],
        [new("PPS awaiting approval", "5", "2 over 3 days", "pink", I.Checkroom), new("Inspections submitted", "17", "This month", "blue", I.FactCheck),
         new("Pending inspections", "6", "Next: 01 Oct", "amber", I.Schedule), new("ASNs generated", "8", "3 arriving today", "green", I.LocalShipping)],
        new("PPS requests to approve", "/pps-list", ["Style", "Vendor", "Category", "Submitted", "Status"],
        [["MS-24-1182", V[0], "Men's shirts", "25 Sep", "Awaiting approval"], ["KT-24-0931", V[1], "Kids' T-shirts", "26 Sep", "Awaiting approval"],
         ["WJ-24-0417", V[5], "Women's jeans", "27 Sep", "Awaiting approval"], ["BS-24-0288", V[2], "Bedsheets", "27 Sep", "Rework requested"], ["TW-24-0102", V[3], "Towels", "28 Sep", "Awaiting approval"]]),
        new("ASNs generated", "/AsnGenList", SideKind.List,
            [Item("ASN 7700451", V[0] + " · 18 cartons · Gurugram DC", "In transit"), Item("ASN 7700448", V[2] + " · 40 cartons · Bhiwandi DC", "In transit"), Item("ASN 7700440", V[3] + " · 22 cartons", "Delivered")]));

    public static readonly DashboardDefinition QC = new(
        "QC dashboard", "Pre-production samples waiting for your quality check.",
        [new("Open PPS requests", "/pps-list", I.Checkroom)],
        [new("PPS to check", "7", "3 received today", "pink", I.Checkroom), new("Checked this week", "19", "16 approved", "green", I.VerifiedUser),
         new("Sent back for rework", "3", "This week", "amber", I.ReportProblem)],
        new("PPS requests to check", "/pps-list", ["Style", "Vendor", "Category", "Received", "Status"],
        [["TW-24-0102", V[3], "Towels", "28 Sep", "Awaiting check"], ["WJ-24-0417", V[5], "Women's jeans", "27 Sep", "Awaiting check"],
         ["KT-24-0931", V[1], "Kids' T-shirts", "26 Sep", "In progress"], ["MS-24-1182", V[0], "Men's shirts", "25 Sep", "Approved"]]),
        new("Recently checked", null, SideKind.List,
            [Item("BS-24-0288", V[2] + " · Bedsheets", "Rework requested"), Item("SH-24-0501", "Prime Leather Goods · Belts", "Approved"), Item("KS-24-0212", V[1] + " · Kids' shorts", "Approved")]));

    public static readonly DashboardDefinition View = new(
        "Overview", "Read-only view of PPS requests and inspection results.",
        [new("View PPS requests", "/pps-list", I.Visibility, false)],
        [new("PPS requests, this month", "58", "41 approved", "blue", I.Checkroom), new("Inspections, this month", "486", "91% passed", "green", I.FactCheck),
         new("Open PPS", "9", "Awaiting decision", "amber", I.Schedule)],
        new("Latest PPS requests", "/pps-list", ["Style", "Vendor", "Category", "Submitted", "Status"],
        [["TW-24-0102", V[3], "Towels", "28 Sep", "Awaiting approval"], ["WJ-24-0417", V[5], "Women's jeans", "27 Sep", "Awaiting approval"],
         ["BS-24-0288", V[2], "Bedsheets", "27 Sep", "Rework requested"], ["SH-24-0501", "Prime Leather Goods", "Belts", "24 Sep", "Approved"]]),
        Pipeline(142, 61, 38, 229, 16), Badge: "Read-only access");

    public static readonly DashboardDefinition ASN = new(
        "ASN dashboard", "ASN requests to process and ASNs generated today.",
        [new("ASN requests", "/AsnReqList", I.LocalShipping)],
        [new("ASN requests waiting", "11", "4 received today", "pink", I.Inbox), new("Generated today", "7", "132 cartons", "green", I.LocalShipping),
         new("Awaiting DC slot", "3", "Gurugram DC", "amber", I.Schedule)],
        new("ASN requests", "/AsnReqList", ["Request", "Vendor", "PO", "Cartons", "Requested", "Status"],
        [["AR-5521", V[1], "4500129051", "64", "30 Sep", "Awaiting generation"], ["AR-5519", V[0], "4500129012", "18", "30 Sep", "Awaiting generation"],
         ["AR-5514", V[2], "4500128790", "40", "29 Sep", "In progress"], ["AR-5509", V[3], "4500129044", "22", "29 Sep", "Generated"]]),
        new("ASNs generated by DC, this week", "/AsnGenList", SideKind.Bars,
            [Bar("Gurugram DC", 24), Bar("Bhiwandi DC", 17), Bar("Kolkata DC", 9), Bar("Hyderabad DC", 6)], Hint: "Count of ASNs."));

    public static readonly DashboardDefinition BFT = new(
        "BFT dashboard", "BFT ASN requests and their status.",
        [],
        [new("BFT ASN requests", "6", "2 received today", "pink", I.Inbox), new("Approved this week", "14", null, "green", I.VerifiedUser),
         new("Waiting for vendor", "2", null, "amber", I.Schedule)],
        new("BFT ASN requests", null, ["Request", "Vendor", "PO", "Requested", "Status"],
        [["BR-311", V[4], "4500129090", "30 Sep", "Awaiting review"], ["BR-309", V[5], "4500129102", "29 Sep", "Awaiting review"], ["BR-305", V[0], "4500129012", "28 Sep", "Approved"]]),
        new("Needs attention", null, SideKind.List,
            [Item("BR-298", V[2] + " · missing packing list", "Pending"), Item("BR-294", V[1] + " · carton count mismatch", "Pending")]));

    public static readonly DashboardDefinition CM = new(
        "CM dashboard", "An overview of inspections and shipments.",
        [],
        [new("Inspection requests, this month", "486", "All DCs", "blue", I.FactCheck), new("Pass rate, 30 days", "91%", null, "green", I.TrendingUp),
         new("ASNs generated, this week", "56", null, "amber", I.LocalShipping)],
        new("Latest inspection results", null, ["Request", "Vendor", "Category", "Date", "Status"],
        [["IR-24690", V[1], "Kids' T-shirts", "24 Sep", "Passed"], ["IR-24661", V[2], "Bedsheets", "22 Sep", "Passed"], ["IR-24612", "Nova Kids Wear", "Girls' dresses", "18 Sep", "Failed"]]),
        Pipeline(142, 61, 38, 229, 16));

    public static readonly DashboardDefinition Vendor = new(
        "Vendor dashboard", "Your inspection requests, their status and ASNs.",
        [new("All my requests", "/AllRecPenOngInspections", I.FactCheck), new("ASN generated", "/AsnGenList", I.LocalShipping, false)],
        [new("Submitted, waiting", "3", "1 received today", "blue", I.FactCheck), new("Pending inspection", "2", "Next: 01 Oct, 11:00", "amber", I.CalendarMonth),
         new("Reports available", "4", "This month", "green", I.Description), new("Failed, 30 days", "1", "Re-inspection booked", "red", I.ReportProblem)],
        new("Your inspection requests", "/AllRecPenOngInspections", ["Request", "PO", "Category", "Date", "Status"],
        [["IR-24807", "4500129051", "Kids' T-shirts", "01 Oct", "Pending"], ["IR-24751", "4500128930", "Kids' shorts", "03 Oct", "Submitted"],
         ["IR-24690", "4500128841", "Kids' T-shirts", "24 Sep", "Passed"], ["IR-24612", "4500128702", "Girls' dresses", "18 Sep", "Failed"], ["IR-24588", "4500128655", "Kids' nightwear", "12 Sep", "Cancelled"]]),
        new("Before your next inspection", null, SideKind.List,
            [Item("Keep 10% of cartons open", "For the random sample on PO 4500129051"), Item("Upload the packing list", "At least one day before", "Pending"), Item("Share the site contact", "Name and phone", "Done")]));

    public static readonly DashboardDefinition NoRole = new(
        "Your dashboard", "Your account has no QPS role yet.",
        [],
        [new("Inspection requests, this month", "486", "All DCs", "blue", I.FactCheck), new("Pass rate, 30 days", "91%", null, "green", I.TrendingUp)],
        new("What you can do", null, ["Step", "Details"],
        [["1", "Ask your manager to request a QPS role"], ["2", "The QPS administrator assigns it in User Management"]]),
        new("Need help?", null, SideKind.List, [Item("QPS support team", "For sign-in or access problems")]));
}
