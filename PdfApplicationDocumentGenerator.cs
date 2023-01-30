using System;
using System.Linq;
using Nml.Improve.Me.Dependencies;

namespace Nml.Improve.Me
{
	public class PdfApplicationDocumentGenerator : IApplicationDocumentGenerator
	{
		private readonly IDataContext _dataContext;
		private IPathProvider _templatePathProvider;
		public IViewGenerator viewGenerator;
		internal readonly IConfiguration configuration;
		private readonly ILogger<PdfApplicationDocumentGenerator> _logger;
		private readonly IPdfGenerator _pdfGenerator;

		public PdfApplicationDocumentGenerator(
			IDataContext dataContext,
			IPathProvider templatePathProvider,
			IViewGenerator viewGenerator,
			IConfiguration configuration,
			IPdfGenerator pdfGenerator,
			ILogger<PdfApplicationDocumentGenerator> logger)
		{	
			_dataContext = dataContext ?? throw new ArgumentNullException(nameof(dataContext));
			_templatePathProvider = templatePathProvider ?? throw new ArgumentNullException(nameof(templatePathProvider));
			this.viewGenerator = viewGenerator;
			this.configuration = configuration;
			_logger = logger ?? throw new ArgumentNullException(nameof(logger));
			_pdfGenerator = pdfGenerator;
		}
		
		public byte[] Generate(Guid applicationId, string baseUri)
		{
			Application application = _dataContext.Applications.Single(app => app.Id == applicationId);

			// Check if the 'application' object is not null, else return a null value
			if (application == null)
			{
				_logger.LogWarning(
					$"No application found for id '{applicationId}'");
				return null;
			}
			
			// Check and transform the 'baseUri' if it ends with the char '/'
			if (baseUri.EndsWith("/"))
			{
				baseUri = baseUri.Substring(baseUri.Length - 1);
			}

			// Check the sate of the application is 'Pending', 'Activated', 'InReview', else return a null value
			if (application.State != ApplicationState.Pending || application.State != ApplicationState.Activated || application.State == ApplicationState.InReview)
			{
				_logger.LogWarning(
					$"The application is in state '{application.State}' and no valid document can be generated for it.");
				return null;
			}

			string view = "";

			if (application.State == ApplicationState.Pending)
			{
				string path = _templatePathProvider.Get("PendingApplication");
				PendingApplicationViewModel vm = new PendingApplicationViewModel
				{
					ReferenceNumber = application.ReferenceNumber,
					State = application.State.ToDescription(),
					FullName = $"{application.Person.FirstName} {application.Person.Surname}",
					AppliedOn = application.Date,
					SupportEmail = configuration.SupportEmail,
					Signature = configuration.Signature
				};
				view = viewGenerator.GenerateFromPath($"{baseUri}{path}", vm);
			}

			if (application.State == ApplicationState.Activated)
			{
				string path = _templatePathProvider.Get("ActivatedApplication");
				ActivatedApplicationViewModel vm = new ActivatedApplicationViewModel
				{
					ReferenceNumber = application.ReferenceNumber,
					State = application.State.ToDescription(),
					FullName = $"{application.Person.FirstName} {application.Person.Surname}",
					LegalEntity = application.IsLegalEntity ? application.LegalEntity : null,
					PortfolioFunds = application.Products.SelectMany(p => p.Funds),
					PortfolioTotalAmount = application.Products.SelectMany(p => p.Funds)
													.Select(f => (f.Amount - f.Fees) * configuration.TaxRate)
													.Sum(),
					AppliedOn = application.Date,
					SupportEmail = configuration.SupportEmail,
					Signature = configuration.Signature
				};
				view = viewGenerator.GenerateFromPath($"{baseUri}{path}", vm);
			}

			if (application.State == ApplicationState.InReview)
			{
				string templatePath = _templatePathProvider.Get("InReviewApplication");
				var inReviewMessage = "Your application has been placed in review" +
									application.CurrentReview.Reason switch
									{
										{ } reason when reason.Contains("address") =>
											" pending outstanding address verification for FICA purposes.",
										{ } reason when reason.Contains("bank") =>
											" pending outstanding bank account verification.",
										_ =>
											" because of suspicious account behaviour. Please contact support ASAP."
									};
				InReviewApplicationViewModel vm = new InReviewApplicationViewModel
				{
					ReferenceNumber = application.ReferenceNumber,
					State = application.State.ToDescription(),
					FullName = $"{application.Person.FirstName} {application.Person.Surname}",
					LegalEntity = application.IsLegalEntity ? application.LegalEntity : null,
					PortfolioFunds = application.Products.SelectMany(p => p.Funds),
					PortfolioTotalAmount = application.Products.SelectMany(p => p.Funds)
													.Select(f => (f.Amount - f.Fees) * configuration.TaxRate)
													.Sum(),
					InReviewMessage = inReviewMessage,
					InReviewInformation = application.CurrentReview,
					AppliedOn = application.Date,
					SupportEmail = configuration.SupportEmail,
					Signature = configuration.Signature
				};
				view = viewGenerator.GenerateFromPath($"{baseUri}{templatePath}", vm);
			}

			var pdfOptions = new PdfOptions
			{
				PageNumbers = PageNumbers.Numeric,
				HeaderOptions = new HeaderOptions
				{
					HeaderRepeat = HeaderRepeat.FirstPageOnly,
					HeaderHtml = PdfConstants.Header
				}
			};

			var pdf = _pdfGenerator.GenerateFromHtml(view, pdfOptions);
			return pdf.ToBytes();
		}
	}
}
