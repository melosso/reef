// AUTO-CONSTRUCTED constant - DO NOT hand-edit the body below.
// Source: /Templates/ERP_Example_-_Invoice_UBL_2.1_ScribanTemplate.xml,
// /Templates/ERP_Example_-_Order_UBL_2.1_ScribanTemplate.xml,
// /Templates/ERP_Example_-_Despatch_Advice_UBL_2.1_ScribanTemplate.xml and
// /Templates/ERP_Example_-_Inventory_UBL_2.1_ScribanTemplate.xml (reference copies retained
// on disk; these are the seed copies copied into QueryTemplates rows at step-execution time).
namespace Reef.Core.Recipes;

internal static class UblExportTemplates
{
    public const string InvoiceXmlTemplate = """""""""""""""
{{~ for row in rows ~}}
<?xml version="1.0" encoding="UTF-8"?>
<Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2"
         xmlns:cac="urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2"
         xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2"
         xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
         xsi:schemaLocation="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2 http://docs.oasis-open.org/ubl/os-ubl-2.1/xsd/maindoc/UBL-Invoice-2.1.xsd">

    <!-- UBL Version and Customization -->
    <cbc:UBLVersionID>{{~ row.ubl_version | html.escape ~}}</cbc:UBLVersionID>
    <cbc:CustomizationID>{{~ row.customization_id | html.escape ~}}</cbc:CustomizationID>

    <!-- Invoice Details -->
    <cbc:ID>{{~ row.invoice_id | html.escape ~}}</cbc:ID>
    <cbc:IssueDate>{{~ row.issue_date | html.escape ~}}</cbc:IssueDate>
    <cbc:InvoiceTypeCode listID="UN/ECE 1001">{{~ row.invoice_type_code | html.escape ~}}</cbc:InvoiceTypeCode>
    <cbc:DocumentCurrencyCode listID="ISO 4217">{{~ row.currency | html.escape ~}}</cbc:DocumentCurrencyCode>

    <!-- Payment Terms -->
    <cac:PaymentMeans>
        <cbc:ID>{{~ row.payment_means_code | html.escape ~}}</cbc:ID>
        <cbc:PaymentMeansCode listID="UN/ECE 4461">{{~ row.payment_means_code | html.escape ~}}</cbc:PaymentMeansCode>
        <cbc:PaymentDueDate>{{~ row.due_date | html.escape ~}}</cbc:PaymentDueDate>
        <cac:PayeeFinancialAccount>
            <cbc:ID>{{~ row.payee_iban | html.escape ~}}</cbc:ID>
        </cac:PayeeFinancialAccount>
    </cac:PaymentMeans>

    <!-- Supplier (Seller) Party -->
    {{~ supplier = parse_json(row.supplier_party_json) ~}}
    <cac:AccountingSupplierParty>
        <cac:Party>
            <cbc:EndpointID schemeID="0192">{{~ supplier.endpoint_id | html.escape ~}}</cbc:EndpointID>
            <cac:PartyName><cbc:Name>{{~ row.supplier_name | html.escape ~}}</cbc:Name></cac:PartyName>
            <cac:PostalAddress>
                <cbc:StreetName>{{~ supplier.street | html.escape ~}}</cbc:StreetName>
                <cbc:CityName>{{~ supplier.city | html.escape ~}}</cbc:CityName>
                <cbc:PostalZone>{{~ supplier.postal | html.escape ~}}</cbc:PostalZone>
                <cac:Country><cbc:IdentificationCode>{{~ supplier.country_code | html.escape ~}}</cbc:IdentificationCode></cac:Country>
            </cac:PostalAddress>
            <cac:PartyTaxScheme>
                <cbc:CompanyID>{{~ supplier.vat_id | html.escape ~}}</cbc:CompanyID>
                <cac:TaxScheme><cbc:ID>VAT</cbc:ID></cac:TaxScheme>
            </cac:PartyTaxScheme>
            <cac:Contact><cbc:ElectronicMail>{{~ supplier.contact_email | html.escape ~}}</cbc:ElectronicMail></cac:Contact>
        </cac:Party>
    </cac:AccountingSupplierParty>

    <!-- Buyer Party -->
    {{~ customer = parse_json(row.customer_party_json) ~}}
    <cac:AccountingCustomerParty>
        <cac:Party>
            <cbc:EndpointID schemeID="0192">{{~ customer.endpoint_id | html.escape ~}}</cbc:EndpointID>
            <cac:PartyName><cbc:Name>{{~ row.customer_name | html.escape ~}}</cbc:Name></cac:PartyName>
            <cac:PostalAddress>
                <cbc:StreetName>{{~ customer.street | html.escape ~}}</cbc:StreetName>
                <cbc:CityName>{{~ customer.city | html.escape ~}}</cbc:CityName>
                <cbc:PostalZone>{{~ customer.postal | html.escape ~}}</cbc:PostalZone>
                <cac:Country><cbc:IdentificationCode>{{~ customer.country_code | html.escape ~}}</cbc:IdentificationCode></cac:Country>
            </cac:PostalAddress>
            <cac:PartyTaxScheme>
                <cbc:CompanyID>{{~ customer.vat_id | html.escape ~}}</cbc:CompanyID>
                <cac:TaxScheme><cbc:ID>VAT</cbc:ID></cac:TaxScheme>
            </cac:PartyTaxScheme>
            <cac:Contact><cbc:ElectronicMail>{{~ customer.contact_email | html.escape ~}}</cbc:ElectronicMail></cac:Contact>
        </cac:Party>
    </cac:AccountingCustomerParty>

    <!-- Tax Total Calculation -->
    {{~ tax_summary = parse_json(row.tax_summary_json) ~}}
    <cac:TaxTotal>
        <cbc:TaxAmount currencyID="{{~ row.currency ~}}">{{~ row.total_tax | html.escape ~}}</cbc:TaxAmount>

        {{~ for tax in tax_summary ~}}
        <cac:TaxSubtotal>
            <cbc:TaxableAmount currencyID="{{~ row.currency ~}}">{{~ tax.taxable_amount | html.escape ~}}</cbc:TaxableAmount>
            <cbc:TaxAmount currencyID="{{~ row.currency ~}}">{{~ tax.tax_amount | html.escape ~}}</cbc:TaxAmount>
            <cac:TaxCategory>
                <cbc:ID schemeID="UN/ECE 5305">{{~ tax.category_code | html.escape ~}}</cbc:ID>
                <cbc:Percent>{{~ tax.tax_rate | html.escape ~}}</cbc:Percent>
                <cac:TaxScheme><cbc:ID>VAT</cbc:ID></cac:TaxScheme>
            </cac:TaxCategory>
        </cac:TaxSubtotal>
        {{~ end ~}}
    </cac:TaxTotal>

    <!-- Legal Monetary Total -->
    <cac:LegalMonetaryTotal>
        <cbc:LineExtensionAmount currencyID="{{~ row.currency ~}}">{{~ row.total_net | html.escape ~}}</cbc:LineExtensionAmount>
        <cbc:TaxExclusiveAmount currencyID="{{~ row.currency ~}}">{{~ row.total_net | html.escape ~}}</cbc:TaxExclusiveAmount>
        <cbc:TaxInclusiveAmount currencyID="{{~ row.currency ~}}">{{~ row.total_gross | html.escape ~}}</cbc:TaxInclusiveAmount>
        <cbc:PayableAmount currencyID="{{~ row.currency ~}}">{{~ row.total_gross | html.escape ~}}</cbc:PayableAmount>
    </cac:LegalMonetaryTotal>

    <!-- Invoice Lines -->
    {{~ line_items = parse_json(row.line_items_json) ~}}
    {{~ for item in line_items ~}}
    <cac:InvoiceLine>
        <cbc:ID>{{~ item.id | html.escape ~}}</cbc:ID>
        <cbc:InvoicedQuantity unitCode="{{~ item.unit_code | html.escape ~}}">{{~ item.quantity | html.escape ~}}</cbc:InvoicedQuantity>
        <cbc:LineExtensionAmount currencyID="{{~ row.currency ~}}">{{~ item.net_total | html.escape ~}}</cbc:LineExtensionAmount>
        <cac:Item>
            <cbc:Description>{{~ item.description | html.escape ~}}</cbc:Description>
            <cbc:Name>{{~ item.description | html.escape ~}}</cbc:Name>
        </cac:Item>
        <cac:Price>
            <cbc:PriceAmount currencyID="{{~ row.currency ~}}">{{~ item.unit_price | html.escape ~}}</cbc:PriceAmount>
        </cac:Price>
        <cac:TaxTotal>
             <cbc:TaxAmount currencyID="{{~ row.currency ~}}">{{ item.net_total | html.escape }}</cbc:TaxAmount>
             <cac:TaxSubtotal>
                 <cbc:TaxableAmount currencyID="{{~ row.currency ~}}">{{ item.net_total | html.escape }}</cbc:TaxableAmount>
                 <cbc:TaxAmount currencyID="{{~ row.currency ~}}">{{ item.tax_rate | html.escape }}</cbc:TaxAmount>
                 <cac:TaxCategory>
                     <cbc:ID schemeID="UN/ECE 5305">S</cbc:ID>
                     <cbc:Percent>{{~ item.tax_rate | html.escape ~}}</cbc:Percent>
                     <cac:TaxScheme><cbc:ID>VAT</cbc:ID></cac:TaxScheme>
                 </cac:TaxCategory>
             </cac:TaxSubtotal>
         </cac:TaxTotal>
    </cac:InvoiceLine>
    {{~ end ~}}

</Invoice>
{{~ end ~}}
""""""""""""""";

    public const string OrderXmlTemplate = """""""""""""""
{{~ for row in rows ~}}
<?xml version="1.0" encoding="UTF-8"?>
<Order xmlns="urn:oasis:names:specification:ubl:schema:xsd:Order-2"
       xmlns:cac="urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2"
       xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2"
       xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
       xsi:schemaLocation="urn:oasis:names:specification:ubl:schema:xsd:Order-2 http://docs.oasis-open.org/ubl/os-ubl-2.1/xsd/maindoc/UBL-Order-2.1.xsd">

    <cbc:UBLVersionID>{{~ row.ubl_version | html.escape ~}}</cbc:UBLVersionID>
    <cbc:CustomizationID>{{~ row.customization_id | html.escape ~}}</cbc:CustomizationID>
    <cbc:ID>{{~ row.order_id | html.escape ~}}</cbc:ID>
    <cbc:IssueDate>{{~ row.issue_date | html.escape ~}}</cbc:IssueDate>
    <cbc:Note>{{~ row.note | html.escape ~}}</cbc:Note>
    <cbc:DocumentCurrencyCode listID="ISO 4217">{{~ row.currency | html.escape ~}}</cbc:DocumentCurrencyCode>

    <!-- Buyer (Customer) -->
    {{~ buyer = parse_json(row.buyer_party_json) ~}}
    <cac:BuyerCustomerParty>
        <cac:Party>
            <cbc:EndpointID schemeID="0192">{{~ buyer.endpoint_id | html.escape ~}}</cbc:EndpointID>
            <cac:PartyName><cbc:Name>{{~ buyer.name | html.escape ~}}</cbc:Name></cac:PartyName>
            <cac:PostalAddress>
                <cbc:StreetName>{{~ buyer.street | html.escape ~}}</cbc:StreetName>
                <cbc:CityName>{{~ buyer.city | html.escape ~}}</cbc:CityName>
                <cbc:PostalZone>{{~ buyer.postal | html.escape ~}}</cbc:PostalZone>
                <cac:Country><cbc:IdentificationCode>{{~ buyer.country_code | html.escape ~}}</cbc:IdentificationCode></cac:Country>
            </cac:PostalAddress>
            <cac:Contact><cbc:ElectronicMail>{{~ buyer.contact_email | html.escape ~}}</cbc:ElectronicMail></cac:Contact>
        </cac:Party>
    </cac:BuyerCustomerParty>

    <!-- Seller (Supplier) -->
    {{~ seller = parse_json(row.seller_party_json) ~}}
    <cac:SellerSupplierParty>
        <cac:Party>
            <cbc:EndpointID schemeID="0192">{{~ seller.endpoint_id | html.escape ~}}</cbc:EndpointID>
            <cac:PartyName><cbc:Name>{{~ seller.name | html.escape ~}}</cbc:Name></cac:PartyName>
            <cac:PostalAddress>
                <cbc:StreetName>{{~ seller.street | html.escape ~}}</cbc:StreetName>
                <cbc:CityName>{{~ seller.city | html.escape ~}}</cbc:CityName>
                <cbc:PostalZone>{{~ seller.postal | html.escape ~}}</cbc:PostalZone>
                <cac:Country><cbc:IdentificationCode>{{~ seller.country_code | html.escape ~}}</cbc:IdentificationCode></cac:Country>
            </cac:PostalAddress>
        </cac:Party>
    </cac:SellerSupplierParty>

    <!-- Delivery -->
    {{~ delivery = parse_json(row.delivery_json) ~}}
    <cac:Delivery>
        <cac:DeliveryLocation>
            <cac:Address>
                <cbc:StreetName>{{~ delivery.street | html.escape ~}}</cbc:StreetName>
                <cbc:CityName>{{~ delivery.city | html.escape ~}}</cbc:CityName>
                <cbc:PostalZone>{{~ delivery.postal | html.escape ~}}</cbc:PostalZone>
                <cac:Country><cbc:IdentificationCode>{{~ delivery.country_code | html.escape ~}}</cbc:IdentificationCode></cac:Country>
            </cac:Address>
        </cac:DeliveryLocation>
        <cac:RequestedDeliveryPeriod>
            <cbc:EndDate>{{~ delivery.delivery_date | html.escape ~}}</cbc:EndDate>
        </cac:RequestedDeliveryPeriod>
    </cac:Delivery>

    <!-- Tax Total -->
    <cac:TaxTotal>
        <cbc:TaxAmount currencyID="{{~ row.currency ~}}">{{~ row.total_tax | html.escape ~}}</cbc:TaxAmount>
    </cac:TaxTotal>

    <!-- Anticipated Monetary Total -->
    <cac:AnticipatedMonetaryTotal>
        <cbc:LineExtensionAmount currencyID="{{~ row.currency ~}}">{{~ row.total_net | html.escape ~}}</cbc:LineExtensionAmount>
        <cbc:TaxExclusiveAmount currencyID="{{~ row.currency ~}}">{{~ row.total_net | html.escape ~}}</cbc:TaxExclusiveAmount>
        <cbc:TaxInclusiveAmount currencyID="{{~ row.currency ~}}">{{~ row.total_gross | html.escape ~}}</cbc:TaxInclusiveAmount>
        <cbc:PayableAmount currencyID="{{~ row.currency ~}}">{{~ row.total_gross | html.escape ~}}</cbc:PayableAmount>
    </cac:AnticipatedMonetaryTotal>

    <!-- Order Lines -->
    {{~ line_items = parse_json(row.line_items_json) ~}}
    {{~ for item in line_items ~}}
    <cac:OrderLine>
        <cac:LineItem>
            <cbc:ID>{{~ item.id | html.escape ~}}</cbc:ID>
            <cbc:Quantity unitCode="{{~ item.unit_code | html.escape ~}}">{{~ item.quantity | html.escape ~}}</cbc:Quantity>
            <cbc:LineExtensionAmount currencyID="{{~ row.currency ~}}">{{~ item.net_total | html.escape ~}}</cbc:LineExtensionAmount>
            <cac:Price>
                <cbc:PriceAmount currencyID="{{~ row.currency ~}}">{{~ item.unit_price | html.escape ~}}</cbc:PriceAmount>
            </cac:Price>
            <cac:Item>
                <cbc:Name>{{~ item.description | html.escape ~}}</cbc:Name>
            </cac:Item>
        </cac:LineItem>
    </cac:OrderLine>
    {{~ end ~}}

</Order>
{{~ end ~}}
""""""""""""""";

    public const string DespatchAdviceXmlTemplate = """""""""""""""
{{~ for row in rows ~}}
<?xml version="1.0" encoding="UTF-8"?>
<DespatchAdvice xmlns="urn:oasis:names:specification:ubl:schema:xsd:DespatchAdvice-2"
                xmlns:cac="urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2"
                xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2"
                xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                xsi:schemaLocation="urn:oasis:names:specification:ubl:schema:xsd:DespatchAdvice-2 http://docs.oasis-open.org/ubl/os-ubl-2.1/xsd/maindoc/UBL-DespatchAdvice-2.1.xsd">

    <cbc:UBLVersionID>{{~ row.ubl_version | html.escape ~}}</cbc:UBLVersionID>
    <cbc:CustomizationID>{{~ row.customization_id | html.escape ~}}</cbc:CustomizationID>
    <cbc:ID>{{~ row.despatch_id | html.escape ~}}</cbc:ID>
    <cbc:IssueDate>{{~ row.issue_date | html.escape ~}}</cbc:IssueDate>
    <cbc:IssueTime>{{~ row.issue_time | html.escape ~}}</cbc:IssueTime>
    <cbc:DespatchAdviceTypeCode>351</cbc:DespatchAdviceTypeCode>
    <cbc:Note>{{~ row.note | html.escape ~}}</cbc:Note>

    <!-- Order Reference -->
    <cac:OrderReference>
        <cbc:ID>{{~ row.order_reference_id | html.escape ~}}</cbc:ID>
    </cac:OrderReference>

    <!-- Supplier (Despatch Party) -->
    {{~ supplier = parse_json(row.supplier_party_json) ~}}
    <cac:DespatchSupplierParty>
        <cac:Party>
            <cbc:EndpointID schemeID="0192">{{~ supplier.endpoint_id | html.escape ~}}</cbc:EndpointID>
            <cac:PartyName><cbc:Name>{{~ supplier.name | html.escape ~}}</cbc:Name></cac:PartyName>
            <cac:PostalAddress>
                <cbc:StreetName>{{~ supplier.street | html.escape ~}}</cbc:StreetName>
                <cbc:CityName>{{~ supplier.city | html.escape ~}}</cbc:CityName>
                <cbc:PostalZone>{{~ supplier.postal | html.escape ~}}</cbc:PostalZone>
                <cac:Country><cbc:IdentificationCode>{{~ supplier.country_code | html.escape ~}}</cbc:IdentificationCode></cac:Country>
            </cac:PostalAddress>
        </cac:Party>
    </cac:DespatchSupplierParty>

    <!-- Customer (Delivery Party) -->
    {{~ delivery = parse_json(row.delivery_party_json) ~}}
    <cac:DeliveryCustomerParty>
        <cac:Party>
            <cbc:EndpointID schemeID="0192">{{~ delivery.endpoint_id | html.escape ~}}</cbc:EndpointID>
            <cac:PartyName><cbc:Name>{{~ delivery.name | html.escape ~}}</cbc:Name></cac:PartyName>
            <cac:PostalAddress>
                <cbc:StreetName>{{~ delivery.street | html.escape ~}}</cbc:StreetName>
                <cbc:CityName>{{~ delivery.city | html.escape ~}}</cbc:CityName>
                <cbc:PostalZone>{{~ delivery.postal | html.escape ~}}</cbc:PostalZone>
                <cac:Country><cbc:IdentificationCode>{{~ delivery.country_code | html.escape ~}}</cbc:IdentificationCode></cac:Country>
            </cac:PostalAddress>
        </cac:Party>
    </cac:DeliveryCustomerParty>

    <!-- Shipment Details -->
    {{~ ship = parse_json(row.shipment_json) ~}}
    <cac:Shipment>
        <cbc:ID>SHP-{{~ row.despatch_id | html.escape ~}}</cbc:ID>
        <cbc:GrossWeightMeasure unitCode="{{~ ship.weight_unit | html.escape ~}}">{{~ ship.gross_weight_measure | html.escape ~}}</cbc:GrossWeightMeasure>
        <cac:Consignment>
            <cbc:ID>{{~ ship.tracking_id | html.escape ~}}</cbc:ID>
            <cac:CarrierParty>
                <cac:PartyName>
                    <cbc:Name>{{~ ship.carrier_name | html.escape ~}}</cbc:Name>
                </cac:PartyName>
            </cac:CarrierParty>
        </cac:Consignment>
    </cac:Shipment>

    <!-- Despatch Lines -->
    {{~ lines = parse_json(row.despatch_lines_json) ~}}
    {{~ for line in lines ~}}
    <cac:DespatchLine>
        <cbc:ID>{{~ line.id | html.escape ~}}</cbc:ID>
        <cbc:DeliveredQuantity unitCode="{{~ line.unit_code | html.escape ~}}">{{~ line.delivered_qty | html.escape ~}}</cbc:DeliveredQuantity>
        <cac:OrderLineReference>
            <cbc:LineID>{{~ line.order_line_id | html.escape ~}}</cbc:LineID>
        </cac:OrderLineReference>
        <cac:Item>
            <cbc:Name>{{~ line.item_name | html.escape ~}}</cbc:Name>
            <cac:SellersItemIdentification>
                <cbc:ID>{{~ line.sellers_item_id | html.escape ~}}</cbc:ID>
            </cac:SellersItemIdentification>
        </cac:Item>
    </cac:DespatchLine>
    {{~ end ~}}

</DespatchAdvice>
{{~ end ~}}
""""""""""""""";

    public const string InventoryXmlTemplate = """""""""""""""
{{~ for row in rows ~}}
<?xml version="1.0" encoding="UTF-8"?>
<InventoryReport xmlns="urn:oasis:names:specification:ubl:schema:xsd:InventoryReport-2"
                 xmlns:cac="urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2"
                 xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2"
                 xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                 xsi:schemaLocation="urn:oasis:names:specification:ubl:schema:xsd:InventoryReport-2 http://docs.oasis-open.org/ubl/os-ubl-2.1/xsd/maindoc/UBL-InventoryReport-2.1.xsd">

    <cbc:UBLVersionID>{{~ row.ubl_version | html.escape ~}}</cbc:UBLVersionID>
    <cbc:CustomizationID>{{~ row.customization_id | html.escape ~}}</cbc:CustomizationID>
    <cbc:ID>{{~ row.report_id | html.escape ~}}</cbc:ID>
    <cbc:IssueDate>{{~ row.issue_date | html.escape ~}}</cbc:IssueDate>
    <cbc:DocumentCurrencyCode>EUR</cbc:DocumentCurrencyCode>

    <!-- Reporting Period -->
    <cac:InventoryPeriod>
        <cbc:StartDate>{{~ row.period_start_date | html.escape ~}}</cbc:StartDate>
        <cbc:EndDate>{{~ row.period_end_date | html.escape ~}}</cbc:EndDate>
    </cac:InventoryPeriod>

    <!-- Retailer (Inventory Owner) -->
    {{~ retailer = parse_json(row.retailer_party_json) ~}}
    <cac:RetailerCustomerParty>
        <cac:Party>
            <cbc:EndpointID schemeID="0192">{{~ retailer.endpoint_id | html.escape ~}}</cbc:EndpointID>
            <cac:PartyName><cbc:Name>{{~ retailer.name | html.escape ~}}</cbc:Name></cac:PartyName>
        </cac:Party>
    </cac:RetailerCustomerParty>

    <!-- Inventory Location -->
    {{~ loc = parse_json(row.location_json) ~}}
    <cac:InventoryLocation>
        <cbc:ID>{{~ loc.id | html.escape ~}}</cbc:ID>
        <cbc:Name>{{~ loc.name | html.escape ~}}</cbc:Name>
        <cac:Address>
            <cbc:StreetName>{{~ loc.street | html.escape ~}}</cbc:StreetName>
            <cbc:CityName>{{~ loc.city | html.escape ~}}</cbc:CityName>
            <cbc:PostalZone>{{~ loc.postal | html.escape ~}}</cbc:PostalZone>
            <cac:Country><cbc:IdentificationCode>{{~ loc.country_code | html.escape ~}}</cbc:IdentificationCode></cac:Country>
        </cac:Address>
    </cac:InventoryLocation>

    <!-- Inventory Lines -->
    {{~ lines = parse_json(row.inventory_lines_json) ~}}
    {{~ for item in lines ~}}
    <cac:InventoryReportLine>
        <cbc:ID>{{~ item.id | html.escape ~}}</cbc:ID>
        <cbc:Quantity unitCode="{{~ item.unit_code | html.escape ~}}">{{~ item.quantity | html.escape ~}}</cbc:Quantity>
        <cbc:InventoryValueAmount currencyID="EUR">0.00</cbc:InventoryValueAmount>
        <cac:Item>
            <cbc:Description>{{~ item.item_name | html.escape ~}}</cbc:Description>
            <cbc:Name>{{~ item.item_name | html.escape ~}}</cbc:Name>
            <cac:SellersItemIdentification>
                <cbc:ID>{{~ item.item_id | html.escape ~}}</cbc:ID>
            </cac:SellersItemIdentification>
        </cac:Item>
        <cac:InventoryLocation>
            <cbc:ID>{{~ item.location_zone | html.escape ~}}</cbc:ID>
        </cac:InventoryLocation>
    </cac:InventoryReportLine>
    {{~ end ~}}

</InventoryReport>
{{~ end ~}}
""""""""""""""";
}
